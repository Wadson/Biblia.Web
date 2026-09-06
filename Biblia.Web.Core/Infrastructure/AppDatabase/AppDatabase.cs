using Biblia.Application.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Biblia.Infrastructure.AppDatabase;

public sealed class AppDatabase : IAppDatabase
{
    public const int CurrentSchemaVersion = 4;
    private readonly ILogger<AppDatabase> _logger;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private bool _initialized;

    public AppDatabase(IAppPaths paths, ILogger<AppDatabase> logger)
        : this(paths.GetPrivateFilePath("bibliatema.db"), logger)
    {
    }

    public AppDatabase(string databasePath, ILogger<AppDatabase> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = Path.GetFullPath(databasePath);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string DatabasePath { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;

        await _initializationGate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            await InitializeCoreAsync(cancellationToken);
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    public async Task ReplaceAsync(string stagedDatabasePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagedDatabasePath);
        var staged = Path.GetFullPath(stagedDatabasePath);
        if (!File.Exists(staged)) throw new FileNotFoundException("Banco preparado para restauração não encontrado.", staged);
        if (!string.Equals(Path.GetDirectoryName(staged), Path.GetDirectoryName(DatabasePath), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("O banco preparado deve estar no mesmo diretório do banco ativo.", nameof(stagedDatabasePath));
        await _initializationGate.WaitAsync(cancellationToken);
        var rollback = DatabasePath + ".restore-rollback";
        try
        {
            SqliteConnection.ClearAllPools();
            DeleteSidecars(DatabasePath);
            if (File.Exists(rollback)) File.Delete(rollback);
            File.Replace(staged, DatabasePath, rollback, ignoreMetadataErrors: true);
            DeleteSidecars(DatabasePath);
            _initialized = false;
            await InitializeCoreAsync(cancellationToken);
            if (File.Exists(rollback)) File.Delete(rollback);
            _logger.LogInformation("Banco do aplicativo restaurado e reinicializado com sucesso.");
        }
        catch
        {
            SqliteConnection.ClearAllPools();
            DeleteSidecars(DatabasePath);
            _initialized = false;
            if (File.Exists(rollback))
            {
                File.Copy(rollback, DatabasePath, overwrite: true);
                await InitializeCoreAsync(CancellationToken.None);
            }
            throw;
        }
        finally
        {
            if (File.Exists(rollback)) File.Delete(rollback);
            _initializationGate.Release();
        }
    }

    private static void DeleteSidecars(string databasePath)
    {
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var sidecar = databasePath + suffix;
            if (File.Exists(sidecar)) File.Delete(sidecar);
        }
    }

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        await using var connection = await OpenConnectionCoreAsync(cancellationToken);
        await ExecuteNonQueryAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken);
        await ExecuteNonQueryAsync(connection, "PRAGMA foreign_keys = ON;", cancellationToken);
        await EnsureMigrationTableAsync(connection, cancellationToken);
        var version = await ReadSchemaVersionAsync(connection, cancellationToken);
        if (version > CurrentSchemaVersion)
            throw new InvalidOperationException($"O banco usa schema {version}, superior ao suportado ({CurrentSchemaVersion}).");
        if (version < 1) await ApplyMigration1Async(connection, cancellationToken);
        if (version < 2) await ApplyMigration2Async(connection, cancellationToken);
        if (version < 3) await ApplyMigration3Async(connection, cancellationToken);
        if (version < 4) await ApplyMigration4Async(connection, cancellationToken);
        _initialized = true;
        _logger.LogInformation("Banco do aplicativo inicializado no schema {SchemaVersion}.", CurrentSchemaVersion);
    }

    public async Task<int> GetSchemaVersionAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenConnectionCoreAsync(cancellationToken);
        return await ReadSchemaVersionAsync(connection, cancellationToken);
    }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var connection = await OpenConnectionCoreAsync(cancellationToken);
        await ExecuteNonQueryAsync(connection, "PRAGMA foreign_keys = ON;", cancellationToken);
        return connection;
    }

    private async Task<SqliteConnection> OpenConnectionCoreAsync(CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        };
        var connection = new SqliteConnection(builder.ToString());
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static Task EnsureMigrationTableAsync(SqliteConnection connection, CancellationToken cancellationToken) =>
        ExecuteNonQueryAsync(connection, """
            CREATE TABLE IF NOT EXISTS SchemaMigration (
                Version INTEGER NOT NULL PRIMARY KEY,
                AppliedAt TEXT NOT NULL
            );
            """, cancellationToken);

    private static async Task<int> ReadSchemaVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM SchemaMigration;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task ApplyMigration1Async(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var transaction = connection.BeginTransaction();
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = Migration1Sql;
            await command.ExecuteNonQueryAsync(cancellationToken);

            command.CommandText = "INSERT INTO SchemaMigration (Version, AppliedAt) VALUES (1, $appliedAt);";
            command.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task ApplyMigration2Async(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var transaction = connection.BeginTransaction();
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = Migration2Sql;
            await command.ExecuteNonQueryAsync(cancellationToken);

            command.CommandText = "INSERT INTO SchemaMigration (Version, AppliedAt) VALUES (2, $appliedAt);";
            command.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task ApplyMigration3Async(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var transaction = connection.BeginTransaction();
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = Migration3Sql;
            await command.ExecuteNonQueryAsync(cancellationToken);
            command.CommandText = "INSERT INTO SchemaMigration (Version, AppliedAt) VALUES (3, $appliedAt);";
            command.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch { await transaction.RollbackAsync(cancellationToken); throw; }
    }

    private static async Task ApplyMigration4Async(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var transaction = connection.BeginTransaction();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            ALTER TABLE ReferenceTheme ADD COLUMN BibleVersionId INTEGER NULL REFERENCES BibleVersionCatalog(Id) ON DELETE RESTRICT;
            UPDATE ReferenceTheme SET BibleVersionId = (
                SELECT PreferredBibleVersionId FROM SavedReference WHERE Id = ReferenceTheme.ReferenceId
            );
            INSERT INTO SchemaMigration(Version, AppliedAt) VALUES(4, $now);
            """;
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task ExecuteNonQueryAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string Migration1Sql = """
        CREATE TABLE Theme (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL COLLATE NOCASE,
            ColorHex TEXT NULL CHECK (ColorHex IS NULL OR ColorHex GLOB '#[0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f]'),
            Description TEXT NULL,
            CreatedAt TEXT NOT NULL,
            UpdatedAt TEXT NOT NULL,
            CONSTRAINT UX_Theme_Name UNIQUE (Name)
        );

        CREATE TABLE BibleVersionCatalog (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Code TEXT NOT NULL COLLATE NOCASE UNIQUE,
            DisplayName TEXT NOT NULL,
            Language TEXT NOT NULL,
            DatabaseFileName TEXT NOT NULL,
            InstalledPath TEXT NULL,
            SchemaVersion INTEGER NOT NULL CHECK (SchemaVersion >= 0),
            LicenseName TEXT NULL,
            LicenseText TEXT NULL,
            Attribution TEXT NULL,
            IsInstalled INTEGER NOT NULL DEFAULT 0 CHECK (IsInstalled IN (0, 1)),
            IsEnabled INTEGER NOT NULL DEFAULT 0 CHECK (IsEnabled IN (0, 1)),
            IsBundled INTEGER NOT NULL DEFAULT 0 CHECK (IsBundled IN (0, 1)),
            InstalledAt TEXT NULL,
            UpdatedAt TEXT NULL,
            ValidationStatus INTEGER NOT NULL DEFAULT 0 CHECK (ValidationStatus BETWEEN 0 AND 3),
            ValidationMessage TEXT NULL
        );

        CREATE TABLE SavedReference (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            BookReferenceId INTEGER NOT NULL CHECK (BookReferenceId BETWEEN 1 AND 66),
            Chapter INTEGER NOT NULL CHECK (Chapter > 0),
            VerseStart INTEGER NOT NULL CHECK (VerseStart > 0),
            VerseEnd INTEGER NOT NULL CHECK (VerseEnd >= VerseStart),
            Comment TEXT NULL,
            PreferredBibleVersionId INTEGER NULL REFERENCES BibleVersionCatalog(Id) ON DELETE SET NULL,
            CreatedAt TEXT NOT NULL,
            UpdatedAt TEXT NOT NULL
        );

        CREATE TABLE ReferenceTheme (
            ReferenceId INTEGER NOT NULL REFERENCES SavedReference(Id) ON DELETE CASCADE,
            ThemeId INTEGER NOT NULL REFERENCES Theme(Id) ON DELETE CASCADE,
            PRIMARY KEY (ReferenceId, ThemeId)
        );

        CREATE TABLE Message (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Title TEXT NOT NULL,
            Description TEXT NULL,
            Type INTEGER NOT NULL CHECK (Type BETWEEN 1 AND 4),
            CreatedAt TEXT NOT NULL,
            UpdatedAt TEXT NOT NULL
        );

        CREATE TABLE MessageTopic (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            MessageId INTEGER NOT NULL REFERENCES Message(Id) ON DELETE CASCADE,
            Title TEXT NOT NULL,
            Content TEXT NULL,
            SortOrder INTEGER NOT NULL CHECK (SortOrder >= 0),
            CONSTRAINT UX_MessageTopic_Order UNIQUE (MessageId, SortOrder)
        );

        CREATE TABLE MessageReference (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            MessageId INTEGER NOT NULL REFERENCES Message(Id) ON DELETE CASCADE,
            ReferenceId INTEGER NOT NULL REFERENCES SavedReference(Id) ON DELETE RESTRICT,
            TopicId INTEGER NULL REFERENCES MessageTopic(Id) ON DELETE SET NULL,
            SortOrder INTEGER NOT NULL CHECK (SortOrder >= 0),
            Observation TEXT NULL,
            PreferredBibleVersionId INTEGER NULL REFERENCES BibleVersionCatalog(Id) ON DELETE SET NULL,
            CONSTRAINT UX_MessageReference_Order UNIQUE (MessageId, SortOrder)
        );

        CREATE TABLE Setting (
            Key TEXT NOT NULL PRIMARY KEY COLLATE NOCASE,
            Value TEXT NOT NULL,
            UpdatedAt TEXT NOT NULL
        );

        CREATE INDEX IX_SavedReference_Canonical ON SavedReference(BookReferenceId, Chapter, VerseStart, VerseEnd);
        CREATE INDEX IX_ReferenceTheme_ThemeId ON ReferenceTheme(ThemeId);
        CREATE INDEX IX_Message_Type_UpdatedAt ON Message(Type, UpdatedAt DESC);
        CREATE INDEX IX_MessageTopic_MessageId ON MessageTopic(MessageId, SortOrder);
        CREATE INDEX IX_MessageReference_ReferenceId ON MessageReference(ReferenceId);
        CREATE INDEX IX_MessageReference_TopicId ON MessageReference(TopicId);
        """;

    private const string Migration2Sql = """
        ALTER TABLE Message ADD COLUMN Introduction TEXT NULL;
        ALTER TABLE Message ADD COLUMN Conclusion TEXT NULL;
        ALTER TABLE Message ADD COLUMN PreferredBibleVersionId INTEGER NULL REFERENCES BibleVersionCatalog(Id) ON DELETE SET NULL;
        """;

    private const string Migration3Sql = """
        ALTER TABLE ReferenceTheme ADD COLUMN Observation TEXT NULL;
        ALTER TABLE ReferenceTheme ADD COLUMN CreatedAt TEXT NULL;
        ALTER TABLE ReferenceTheme ADD COLUMN UpdatedAt TEXT NULL;
        UPDATE ReferenceTheme
        SET CreatedAt = COALESCE(CreatedAt, strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
            UpdatedAt = COALESCE(UpdatedAt, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
        DROP TABLE IF EXISTS MessageReference;
        DROP TABLE IF EXISTS MessageTopic;
        DROP TABLE IF EXISTS Message;
        CREATE INDEX IF NOT EXISTS IX_ReferenceTheme_ThemeBookOrder ON ReferenceTheme(ThemeId, ReferenceId);
        """;
}
