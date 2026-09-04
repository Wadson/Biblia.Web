using Biblia.Application.Interfaces;
using Biblia.Domain.Entities;
using Biblia.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Biblia.Infrastructure.BibleDatabases;

public sealed class BibleValidationService(ILogger<BibleValidationService> logger) : IBibleValidationService
{
    private static readonly IReadOnlyDictionary<string, string[]> RequiredColumns = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["book"] = ["id", "book_reference_id", "testament_reference_id", "name"],
        ["metadata"] = ["key", "value"],
        ["verse"] = ["id", "book_id", "chapter", "verse", "text"]
    };

    public async Task<BibleValidationResult> ValidateAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath))
            return Incompatible("Arquivo não encontrado.", issues);

        try
        {
            var cs = new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(databasePath), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
            await using var connection = new SqliteConnection(cs);
            await connection.OpenAsync(cancellationToken);

            var integrity = await ScalarStringAsync(connection, "PRAGMA integrity_check;", cancellationToken);
            if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase)) issues.Add($"Falha de integridade: {integrity}.");

            foreach (var (table, columns) in RequiredColumns)
            {
                if (!await TableExistsAsync(connection, table, cancellationToken)) { issues.Add($"Tabela obrigatória ausente: {table}."); continue; }
                var actual = await GetColumnsAsync(connection, table, cancellationToken);
                foreach (var column in columns.Where(column => !actual.Contains(column))) issues.Add($"Coluna obrigatória ausente: {table}.{column}.");
            }

            if (issues.Count > 0) return Incompatible(null, issues);

            var metadata = await ReadMetadataAsync(connection, cancellationToken);
            metadata.TryGetValue("name", out var name);
            metadata.TryGetValue("dbversion", out var dbVersionText);
            _ = int.TryParse(dbVersionText, out var schemaVersion);
            if (string.IsNullOrWhiteSpace(name)) issues.Add("Metadata 'name' ausente.");
            if (schemaVersion <= 0) issues.Add("Metadata 'dbversion' inválida.");

            var books = await ScalarIntAsync(connection, "SELECT COUNT(*) FROM book;", cancellationToken);
            var verses = await ScalarIntAsync(connection, "SELECT COUNT(*) FROM verse;", cancellationToken);
            var duplicates = await ScalarIntAsync(connection, "SELECT COALESCE(SUM(c-1),0) FROM (SELECT COUNT(*) c FROM verse GROUP BY book_id,chapter,verse HAVING COUNT(*)>1);", cancellationToken);
            var invalid = await ScalarIntAsync(connection, "SELECT COUNT(*) FROM verse WHERE chapter<1 OR verse<1 OR text IS NULL OR trim(text)='';", cancellationToken);
            var orphans = await ScalarIntAsync(connection, "SELECT COUNT(*) FROM verse v LEFT JOIN book b ON b.id=v.book_id WHERE b.id IS NULL;", cancellationToken);
            if (books != 66) issues.Add($"Quantidade de livros inesperada: {books}.");
            if (verses is < 30000 or > 32000) issues.Add($"Quantidade de versículos inesperada: {verses}.");
            if (duplicates > 0) issues.Add($"Referências duplicadas: {duplicates}.");
            if (invalid > 0) issues.Add($"Versículos inválidos: {invalid}.");
            if (orphans > 0) issues.Add($"Referências órfãs: {orphans}.");

            var status = issues.Count == 0 ? BibleVersionValidationStatus.Compatible : BibleVersionValidationStatus.Incompatible;
            return new(status, Path.GetFileNameWithoutExtension(databasePath).ToUpperInvariant(), name, schemaVersion, books, verses, duplicates, issues);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Falha ao validar banco bíblico {DatabasePath}.", databasePath);
            issues.Add($"Não foi possível abrir ou validar o banco: {ex.Message}");
            return Incompatible(null, issues);
        }
    }

    private static BibleValidationResult Incompatible(string? issue, List<string> issues){if(issue is not null)issues.Add(issue);return new(BibleVersionValidationStatus.Incompatible,null,null,0,0,0,0,issues);}
    private static async Task<bool> TableExistsAsync(SqliteConnection c,string table,CancellationToken t){await using var cmd=c.CreateCommand();cmd.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";cmd.Parameters.AddWithValue("$name",table);return Convert.ToInt32(await cmd.ExecuteScalarAsync(t))==1;}
    private static async Task<HashSet<string>> GetColumnsAsync(SqliteConnection c,string table,CancellationToken t){var result=new HashSet<string>(StringComparer.OrdinalIgnoreCase);await using var cmd=c.CreateCommand();cmd.CommandText=$"PRAGMA table_info([{table}]);";await using var r=await cmd.ExecuteReaderAsync(t);while(await r.ReadAsync(t))result.Add(r.GetString(1));return result;}
    private static async Task<Dictionary<string,string>> ReadMetadataAsync(SqliteConnection c,CancellationToken t){var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);await using var cmd=c.CreateCommand();cmd.CommandText="SELECT key,value FROM metadata;";await using var r=await cmd.ExecuteReaderAsync(t);while(await r.ReadAsync(t))result[r.GetString(0)]=r.GetString(1);return result;}
    private static async Task<int> ScalarIntAsync(SqliteConnection c,string sql,CancellationToken t){await using var cmd=c.CreateCommand();cmd.CommandText=sql;return Convert.ToInt32(await cmd.ExecuteScalarAsync(t));}
    private static async Task<string?> ScalarStringAsync(SqliteConnection c,string sql,CancellationToken t){await using var cmd=c.CreateCommand();cmd.CommandText=sql;return Convert.ToString(await cmd.ExecuteScalarAsync(t));}
}
