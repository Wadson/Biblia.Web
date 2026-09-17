using Biblia.Application.Interfaces;
using Biblia.Infrastructure.Files;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Biblia.Infrastructure.AppDatabase;

public sealed class AppDatabase : IAppDatabase
{
    public const int CurrentSchemaVersion = 8;
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
        var staged=Path.GetFullPath(stagedDatabasePath);
        if(!File.Exists(staged)||staged.Equals(DatabasePath,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Banco preparado inválido.");
        await _initializationGate.WaitAsync(cancellationToken);
        try
        {
            using var exclusive=await ConnectionLease(true,cancellationToken);
            await using var source=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=staged,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());
            await using var target=await OpenConnectionCoreAsync(cancellationToken,false);
            await source.OpenAsync(cancellationToken);
            await using(var check=source.CreateCommand())
            {
                check.CommandText="PRAGMA integrity_check;";
                if(Convert.ToString(await check.ExecuteScalarAsync(cancellationToken))!="ok")throw new InvalidDataException("O banco preparado está corrompido.");
            }
            // SQLite performs the replacement as a destination transaction, preserving WAL semantics.
            // Never unlink a live WAL/SHM file or swap an inode behind pooled connections.
            source.BackupDatabase(target);
            _initialized=false;
        }
        finally{_initializationGate.Release();}
        await InitializeAsync(cancellationToken);
    }

    private async Task<IDisposable> ConnectionLease(bool exclusive,CancellationToken ct)
    {
        var path=DatabasePath+".connections.lock";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var timeoutSource=new CancellationTokenSource(FileOperationLease.DefaultTimeout);
        using var waitSource=CancellationTokenSource.CreateLinkedTokenSource(ct,timeoutSource.Token);
        while(true)
        {
            if (!ct.IsCancellationRequested && timeoutSource.IsCancellationRequested)
            {
                _logger.LogWarning("Tempo limite ao aguardar {LockType} do banco {DatabasePath}.", exclusive ? "acesso exclusivo" : "acesso compartilhado", DatabasePath);
                throw new TimeoutException("O banco está ocupado por outra operação. Tente novamente em alguns instantes.");
            }
            waitSource.Token.ThrowIfCancellationRequested();
            try
            {
                if(!File.Exists(path)){using var create=new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.ReadWrite);}
                return new FileStream(path,FileMode.Open,exclusive?FileAccess.ReadWrite:FileAccess.Read,exclusive?FileShare.None:FileShare.Read);
            }
            catch(IOException)
            {
                try { await Task.Delay(50,waitSource.Token); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeoutSource.IsCancellationRequested)
                {
                    _logger.LogWarning("Tempo limite ao aguardar {LockType} do banco {DatabasePath}.", exclusive ? "acesso exclusivo" : "acesso compartilhado", DatabasePath);
                    throw new TimeoutException("O banco está ocupado por outra operação. Tente novamente em alguns instantes.");
                }
            }
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
        if (version < 5) await ApplyMigration5Async(connection, cancellationToken);
        if (version < 6) await ApplyMigration6Async(connection, cancellationToken);
        if (version < 7) await ApplyMigration7Async(connection, cancellationToken);
        if (version < 8) await ApplyMigration8Async(connection, cancellationToken);
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

    private async Task<SqliteConnection> OpenConnectionCoreAsync(CancellationToken cancellationToken, bool tracked=true)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        };
        var lease=tracked?await ConnectionLease(false,cancellationToken):null;
        var connection = new SqliteConnection(builder.ToString());
        connection.StateChange += (_,e)=>{if(e.CurrentState==System.Data.ConnectionState.Closed)lease?.Dispose();};
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            lease?.Dispose();
            throw;
        }
    }

    private static async Task ApplyMigration5Async(SqliteConnection connection, CancellationToken ct)
    {
        await using var tx=connection.BeginTransaction();
        await using var cmd=connection.CreateCommand(); cmd.Transaction=tx;
        cmd.CommandText="""
            ALTER TABLE Theme ADD COLUMN OrderingMode INTEGER NOT NULL DEFAULT 0 CHECK(OrderingMode IN(0,1));
            CREATE TABLE ThemeContent(
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ThemeId INTEGER NOT NULL REFERENCES Theme(Id) ON DELETE CASCADE,
                ReferenceId INTEGER NULL,
                SortOrder INTEGER NOT NULL CHECK(SortOrder>=0),
                BlockJson TEXT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                FOREIGN KEY(ReferenceId,ThemeId) REFERENCES ReferenceTheme(ReferenceId,ThemeId) ON DELETE CASCADE,
                CHECK((ReferenceId IS NOT NULL AND BlockJson IS NULL) OR (ReferenceId IS NULL AND BlockJson IS NOT NULL)),
                UNIQUE(ThemeId,ReferenceId)
            );
            CREATE UNIQUE INDEX UX_ThemeContent_Position ON ThemeContent(ThemeId,SortOrder);
            INSERT INTO ThemeContent(ThemeId,ReferenceId,SortOrder,CreatedAt,UpdatedAt)
            SELECT rt.ThemeId,rt.ReferenceId,ROW_NUMBER() OVER(PARTITION BY rt.ThemeId ORDER BY r.BookReferenceId,r.Chapter,r.VerseStart,r.VerseEnd,r.Id)-1,
              COALESCE(rt.CreatedAt,$now),COALESCE(rt.UpdatedAt,$now)
            FROM ReferenceTheme rt JOIN SavedReference r ON r.Id=rt.ReferenceId;
            CREATE TRIGGER ThemeContent_LinkInserted AFTER INSERT ON ReferenceTheme BEGIN
              INSERT INTO ThemeContent(ThemeId,ReferenceId,SortOrder,CreatedAt,UpdatedAt)
              VALUES(NEW.ThemeId,NEW.ReferenceId,COALESCE((SELECT MAX(SortOrder)+1 FROM ThemeContent WHERE ThemeId=NEW.ThemeId),0),
                COALESCE(NEW.CreatedAt,strftime('%Y-%m-%dT%H:%M:%fZ','now')),COALESCE(NEW.UpdatedAt,strftime('%Y-%m-%dT%H:%M:%fZ','now')));
            END;
            INSERT INTO SchemaMigration(Version,AppliedAt) VALUES(5,$now);
            """;
        cmd.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct); await tx.CommitAsync(ct);
    }
    private static async Task ApplyMigration6Async(SqliteConnection connection,CancellationToken ct)
    {
        await using var tx=connection.BeginTransaction(); await using var cmd=connection.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="""
            CREATE TABLE IF NOT EXISTS Publication(Id INTEGER PRIMARY KEY AUTOINCREMENT,Name TEXT NOT NULL COLLATE NOCASE UNIQUE,Title TEXT NULL,Subtitle TEXT NULL,HeaderText TEXT NULL,HeaderBackgroundColorHex TEXT NULL CHECK(HeaderBackgroundColorHex IS NULL OR HeaderBackgroundColorHex GLOB '#[0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f]'),HeaderTextColorHex TEXT NULL CHECK(HeaderTextColorHex IS NULL OR HeaderTextColorHex GLOB '#[0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f]'),TitleFontSize REAL NULL CHECK(TitleFontSize IS NULL OR TitleFontSize BETWEEN 8 AND 72),TitleBold INTEGER NOT NULL DEFAULT 0,TitleItalic INTEGER NOT NULL DEFAULT 0,BrandingId INTEGER NULL,CreatedAt TEXT NOT NULL,UpdatedAt TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS OrganizationBranding(Id INTEGER PRIMARY KEY AUTOINCREMENT,Name TEXT NOT NULL,Logo BLOB NULL,LogoContentType TEXT NULL,CreatedAt TEXT NOT NULL,UpdatedAt TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS PublicationThemeReference(PublicationId INTEGER NOT NULL REFERENCES Publication(Id) ON DELETE CASCADE,ThemeId INTEGER NOT NULL,ReferenceId INTEGER NOT NULL,CreatedAt TEXT NOT NULL,UpdatedAt TEXT NOT NULL,PRIMARY KEY(PublicationId,ThemeId,ReferenceId),FOREIGN KEY(ReferenceId,ThemeId) REFERENCES ReferenceTheme(ReferenceId,ThemeId) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS PublicationContent(Id INTEGER PRIMARY KEY AUTOINCREMENT,PublicationId INTEGER NOT NULL REFERENCES Publication(Id) ON DELETE CASCADE,ThemeId INTEGER NOT NULL REFERENCES Theme(Id) ON DELETE CASCADE,ReferenceId INTEGER NULL,SortOrder INTEGER NOT NULL CHECK(SortOrder>=0),BlockJson TEXT NULL,CreatedAt TEXT NOT NULL,UpdatedAt TEXT NOT NULL,CHECK((ReferenceId IS NOT NULL AND BlockJson IS NULL) OR (ReferenceId IS NULL AND BlockJson IS NOT NULL)),UNIQUE(PublicationId,ThemeId,ReferenceId),UNIQUE(PublicationId,ThemeId,SortOrder));
            CREATE INDEX IF NOT EXISTS IX_PublicationThemeReference_Publication ON PublicationThemeReference(PublicationId,ThemeId);
            INSERT INTO Publication(Name,CreatedAt,UpdatedAt) SELECT 'Temas e Versículos',$now,$now WHERE NOT EXISTS(SELECT 1 FROM Publication);
            INSERT OR IGNORE INTO PublicationThemeReference(PublicationId,ThemeId,ReferenceId,CreatedAt,UpdatedAt) SELECT (SELECT Id FROM Publication ORDER BY Id LIMIT 1),ThemeId,ReferenceId,CreatedAt,UpdatedAt FROM ReferenceTheme;
            INSERT OR IGNORE INTO PublicationContent(PublicationId,ThemeId,ReferenceId,SortOrder,CreatedAt,UpdatedAt) SELECT (SELECT Id FROM Publication ORDER BY Id LIMIT 1),ThemeId,ReferenceId,SortOrder,CreatedAt,UpdatedAt FROM ThemeContent WHERE ReferenceId IS NOT NULL;
            INSERT OR IGNORE INTO PublicationContent(PublicationId,ThemeId,BlockJson,SortOrder,CreatedAt,UpdatedAt) SELECT (SELECT Id FROM Publication ORDER BY Id LIMIT 1),ThemeId,BlockJson,SortOrder,CreatedAt,UpdatedAt FROM ThemeContent WHERE BlockJson IS NOT NULL;
            INSERT INTO SchemaMigration(Version,AppliedAt) VALUES(6,$now);
            """;cmd.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToString("O"));await cmd.ExecuteNonQueryAsync(ct);await tx.CommitAsync(ct);
    }
    private static async Task ApplyMigration7Async(SqliteConnection connection,CancellationToken ct)
    {await using var tx=connection.BeginTransaction();var columns=new[]{("OrganizationFontSize","REAL NULL CHECK(OrganizationFontSize IS NULL OR OrganizationFontSize BETWEEN 7 AND 36)"),("OrganizationTextColorHex","TEXT NULL"),("SubtitleFontSize","REAL NULL CHECK(SubtitleFontSize IS NULL OR SubtitleFontSize BETWEEN 7 AND 36)"),("SubtitleTextColorHex","TEXT NULL"),("HeaderDetailFontSize","REAL NULL CHECK(HeaderDetailFontSize IS NULL OR HeaderDetailFontSize BETWEEN 7 AND 36)"),("HeaderDetailTextColorHex","TEXT NULL"),("LogoWidth","REAL NULL CHECK(LogoWidth IS NULL OR LogoWidth BETWEEN 20 AND 160)"),("LogoHeight","REAL NULL CHECK(LogoHeight IS NULL OR LogoHeight BETWEEN 20 AND 100)")};foreach(var column in columns)if(!await ColumnExistsAsync(connection,column.Item1,ct)){await using var alter=connection.CreateCommand();alter.Transaction=tx;alter.CommandText=$"ALTER TABLE Publication ADD COLUMN {column.Item1} {column.Item2};";await alter.ExecuteNonQueryAsync(ct);}await using var cmd=connection.CreateCommand();cmd.Transaction=tx;cmd.CommandText="INSERT INTO SchemaMigration(Version,AppliedAt) VALUES(7,$now);";cmd.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToString("O"));await cmd.ExecuteNonQueryAsync(ct);await tx.CommitAsync(ct);}
    private static async Task ApplyMigration8Async(SqliteConnection connection,CancellationToken ct)
    {await using var tx=connection.BeginTransaction();if(!await ColumnExistsAsync(connection,"TitleTextColorHex",ct)){await using var alter=connection.CreateCommand();alter.Transaction=tx;alter.CommandText="ALTER TABLE Publication ADD COLUMN TitleTextColorHex TEXT NULL;";await alter.ExecuteNonQueryAsync(ct);}await using var cmd=connection.CreateCommand();cmd.Transaction=tx;cmd.CommandText="""UPDATE Publication SET TitleTextColorHex=HeaderTextColorHex WHERE TitleTextColorHex IS NULL;UPDATE Publication SET OrganizationTextColorHex=HeaderTextColorHex WHERE OrganizationTextColorHex IS NULL;UPDATE Publication SET SubtitleTextColorHex=HeaderTextColorHex WHERE SubtitleTextColorHex IS NULL;UPDATE Publication SET HeaderDetailTextColorHex=HeaderTextColorHex WHERE HeaderDetailTextColorHex IS NULL;INSERT INTO SchemaMigration(Version,AppliedAt) VALUES(8,$now);""";cmd.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToString("O"));await cmd.ExecuteNonQueryAsync(ct);await tx.CommitAsync(ct);}

    private static async Task<bool> ColumnExistsAsync(SqliteConnection connection,string column,CancellationToken ct)
    {await using var cmd=connection.CreateCommand();cmd.CommandText="PRAGMA table_info(Publication);";await using var reader=await cmd.ExecuteReaderAsync(ct);while(await reader.ReadAsync(ct))if(string.Equals(reader.GetString(1),column,StringComparison.OrdinalIgnoreCase))return true;return false;}

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
