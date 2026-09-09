using System.IO.Compression;
using System.Text.Json;
using System.Security.Cryptography;
using Biblia.Application.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using AppUserDatabase = Biblia.Infrastructure.AppDatabase.AppDatabase;

namespace Biblia.Infrastructure.Files;

public sealed class BackupService(IAppDatabase database, IAppPaths paths, ILogger<BackupService> logger) : IBackupService
{
    public async Task<BackupInfo> CreateAsync(CancellationToken cancellationToken = default)
    {
        await database.InitializeAsync(cancellationToken);
        var schema = await database.GetSchemaVersionAsync(cancellationToken);
        var created = DateTimeOffset.UtcNow;
        var directory = Path.Combine(paths.AppDataDirectory, "backups");
        Directory.CreateDirectory(directory);
        var backupPath = Path.Combine(directory, $"bibliatema-{created:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.zip");
        var snapshotPath = Path.Combine(directory, $".{Guid.NewGuid():N}.db");
        try
        {
            await CreateSnapshotAsync(snapshotPath, cancellationToken);
            var snapshotInfo = new FileInfo(snapshotPath);
            var checksum = await ComputeHashAsync(snapshotPath, cancellationToken);
            await using (var output = new FileStream(backupPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false))
            {
                var manifest = archive.CreateEntry("manifest.json");
                await using (var manifestStream = manifest.Open())
                    await JsonSerializer.SerializeAsync(manifestStream, new Manifest(created, schema, "1.0.0", snapshotInfo.Length, checksum), cancellationToken: cancellationToken);
                var entry = archive.CreateEntry("bibliatema.db", CompressionLevel.Optimal);
                await using var input = new FileStream(snapshotPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                await using var target = entry.Open();
                await input.CopyToAsync(target, cancellationToken);
            }
            logger.LogInformation("Backup criado em {BackupPath}, schema {SchemaVersion}", backupPath, schema);
            return new BackupInfo(backupPath, created, schema, new FileInfo(backupPath).Length);
        }
        catch
        {
            if (File.Exists(backupPath)) File.Delete(backupPath);
            throw;
        }
        finally
        {
            if (File.Exists(snapshotPath)) File.Delete(snapshotPath);
        }
    }

    public async Task<BackupInfo> ValidateAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        if (!File.Exists(backupPath)) throw new FileNotFoundException("Arquivo de backup não encontrado.", backupPath);
        await using var file = new FileStream(backupPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false);
        if (archive.Entries.Any(x => x.FullName.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(x.FullName))) throw new InvalidDataException("Backup contém caminho inseguro.");
        if (archive.Entries.Sum(x => x.Length) > 500_000_000) throw new InvalidDataException("Conteúdo do backup excede o limite permitido.");
        var manifest = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("Backup sem manifesto.");
        var databaseEntry = archive.GetEntry("bibliatema.db") ?? throw new InvalidDataException("Backup sem banco do usuário.");
        if (archive.Entries.Count > 20) throw new InvalidDataException("Quantidade de arquivos inválida no backup.");
        if (databaseEntry.Length <= 0) throw new InvalidDataException("O banco do backup está vazio.");
        if (databaseEntry.Length > 250_000_000) throw new InvalidDataException("O banco do backup excede o limite permitido.");
        await using var stream = manifest.Open();
        var data = await JsonSerializer.DeserializeAsync<Manifest>(stream, cancellationToken: cancellationToken) ?? throw new InvalidDataException("Manifesto inválido.");
        if (data.SchemaVersion <= 0) throw new InvalidDataException("Versão de schema inválida.");
        if (data.SchemaVersion > AppUserDatabase.CurrentSchemaVersion) throw new InvalidDataException("Backup usa schema mais novo.");
        if (data.DatabaseSize > 0 && data.DatabaseSize != databaseEntry.Length) throw new InvalidDataException("Tamanho do banco não confere com o manifesto.");
        if (!string.IsNullOrWhiteSpace(data.DatabaseSha256))
        {
            await using var databaseStream = databaseEntry.Open();
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(databaseStream, cancellationToken));
            if (!actual.Equals(data.DatabaseSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Checksum do banco não confere.");
        }
        return new BackupInfo(backupPath, data.CreatedAt, data.SchemaVersion, new FileInfo(backupPath).Length);
    }

    public async Task RestoreAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(backupPath, cancellationToken);
        var safety = await CreateAsync(cancellationToken);
        var staged = database.DatabasePath + ".restore";
        try
        {
            await ExtractDatabaseAsync(backupPath, staged, cancellationToken);
            await ValidateDatabaseAsync(staged, cancellationToken);
            await database.ReplaceAsync(staged, cancellationToken);
            logger.LogInformation("Backup {BackupPath} restaurado; backup de segurança: {SafetyPath}", backupPath, safety.Path);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao restaurar {BackupPath}; o banco ativo foi preservado", backupPath);
            throw;
        }
        finally
        {
            if (File.Exists(staged)) File.Delete(staged);
            foreach (var suffix in new[] { "-wal", "-shm" })
            {
                var sidecar = staged + suffix;
                if (File.Exists(sidecar)) File.Delete(sidecar);
            }
        }
    }

    private async Task CreateSnapshotAsync(string destination, CancellationToken cancellationToken)
    {
        var sourceBuilder = new SqliteConnectionStringBuilder { DataSource = database.DatabasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
        var targetBuilder = new SqliteConnectionStringBuilder { DataSource = destination, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false };
        await using var source = new SqliteConnection(sourceBuilder.ToString());
        await using var target = new SqliteConnection(targetBuilder.ToString());
        await source.OpenAsync(cancellationToken);
        await target.OpenAsync(cancellationToken);
        source.BackupDatabase(target);
    }

    private static async Task ExtractDatabaseAsync(string backupPath, string destination, CancellationToken cancellationToken)
    {
        if (File.Exists(destination)) File.Delete(destination);
        await using var file = new FileStream(backupPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false);
        await using var source = archive.GetEntry("bibliatema.db")!.Open();
        await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await source.CopyToAsync(target, cancellationToken);
        await target.FlushAsync(cancellationToken);
    }

    private static async Task ValidateDatabaseAsync(string path, CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
        await using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken);
        await using var integrity = connection.CreateCommand();
        integrity.CommandText = "PRAGMA integrity_check;";
        if (!string.Equals(Convert.ToString(await integrity.ExecuteScalarAsync(cancellationToken)), "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("O banco contido no backup está corrompido.");
        await using var foreignKeys = connection.CreateCommand();
        foreignKeys.CommandText = "PRAGMA foreign_key_check;";
        await using var violations = await foreignKeys.ExecuteReaderAsync(cancellationToken);
        if (await violations.ReadAsync(cancellationToken)) throw new InvalidDataException("O banco contido no backup viola a integridade referencial.");
        await using var schema = connection.CreateCommand();
        schema.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM SchemaMigration;";
        var version = Convert.ToInt32(await schema.ExecuteScalarAsync(cancellationToken));
        if (version <= 0 || version > AppUserDatabase.CurrentSchemaVersion)
            throw new InvalidDataException("O banco contido no backup usa um schema incompatível.");
    }

    private static async Task<string> ComputeHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken));
    }

    private sealed record Manifest(DateTimeOffset CreatedAt, int SchemaVersion, string AppVersion = "1.0.0", long DatabaseSize = 0, string? DatabaseSha256 = null);
}
