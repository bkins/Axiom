using Axiom.Core;
using Microsoft.Data.Sqlite;

namespace Axiom.Infrastructure;

public sealed partial class SqliteStateStore(AppDataPaths paths, IAxiomLog? log = null, bool? caseSensitivePaths = null) : IRecoveryStore, IDocumentMetadataStore, IWorkspaceStore, IAppearanceStore, IDocumentMutationStore, IWorkspaceCatalogStore, IDeletedDocumentStore
{
    public const int CurrentSchemaVersion = 9;
    private bool CaseSensitivePaths => caseSensitivePaths ?? !OperatingSystem.IsWindows();
    private string PathCollation => CaseSensitivePaths ? "BINARY" : "NOCASE";
    private StringComparison PathComparison => CaseSensitivePaths ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
    public AppDataPaths Paths => paths;
    private readonly IAxiomLog _log = log ?? new NullAxiomLog();
    private string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = paths.DatabasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared,
        Pooling = false
    }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        paths.EnsureCreated();
        BackupExistingDatabase();
        await using var connection = await OpenAsync(cancellationToken);
        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS SchemaInfo (Version INTEGER NOT NULL);
            INSERT INTO SchemaInfo(Version) SELECT 1 WHERE NOT EXISTS (SELECT 1 FROM SchemaInfo);
            CREATE TABLE IF NOT EXISTS AppSessions (
                SessionId TEXT PRIMARY KEY,
                StartedUtc TEXT NOT NULL,
                EndedUtc TEXT NULL,
                CleanExit INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS RecoveryDrafts (
                DraftKey TEXT PRIMARY KEY,
                FilePath TEXT NULL,
                TextContent TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                SavedFileWriteUtc TEXT NULL,
                IsNewDocument INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS DocumentMetadata (
                DocumentId TEXT NOT NULL,
                FilePath TEXT NOT NULL COLLATE NOCASE,
                LastSeenUtc TEXT NOT NULL,
                PRIMARY KEY (DocumentId, FilePath)
            );
            CREATE INDEX IF NOT EXISTS IX_DocumentMetadata_Path ON DocumentMetadata(FilePath);
            """, cancellationToken);
        var schemaVersion = await GetSchemaVersionAsync(connection, cancellationToken);
        if (schemaVersion > CurrentSchemaVersion)
            throw new InvalidOperationException($"The Axiom state database schema {schemaVersion} is newer than supported schema {CurrentSchemaVersion}.");
        if (schemaVersion < 2)
        {
            await ExecuteAsync(connection, """
                CREATE TABLE IF NOT EXISTS Workspaces (
                    WorkspaceId TEXT PRIMARY KEY,
                    RootPath TEXT NOT NULL COLLATE NOCASE UNIQUE,
                    DisplayName TEXT NOT NULL,
                    CreatedUtc TEXT NOT NULL,
                    LastOpenedUtc TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS ActiveWorkspace (
                    SingletonId INTEGER PRIMARY KEY CHECK (SingletonId = 1),
                    WorkspaceId TEXT NOT NULL,
                    FOREIGN KEY (WorkspaceId) REFERENCES Workspaces(WorkspaceId) ON DELETE CASCADE
                );
                UPDATE SchemaInfo SET Version = 2;
                """, cancellationToken);
        }
        if (schemaVersion < 3)
        {
            await ExecuteAsync(connection, """
                CREATE TABLE IF NOT EXISTS WorkspacePins (
                    WorkspaceId TEXT NOT NULL,
                    RelativePath TEXT NOT NULL COLLATE NOCASE,
                    PinnedUtc TEXT NOT NULL,
                    PRIMARY KEY (WorkspaceId, RelativePath),
                    FOREIGN KEY (WorkspaceId) REFERENCES Workspaces(WorkspaceId) ON DELETE CASCADE
                );
                UPDATE SchemaInfo SET Version = 3;
                """, cancellationToken);
        }
        if (schemaVersion < 4)
        {
            await ExecuteAsync(connection, """
                CREATE TABLE IF NOT EXISTS AppSettings (
                    SettingKey TEXT PRIMARY KEY,
                    SettingValue TEXT NOT NULL
                );
                UPDATE SchemaInfo SET Version = 4;
                """, cancellationToken);
        }
        if (schemaVersion < 5)
        {
            await ExecuteAsync(connection, """
                CREATE TABLE IF NOT EXISTS DocumentMutations (
                    OperationId TEXT PRIMARY KEY,
                    IntentJson TEXT NOT NULL
                );
                UPDATE SchemaInfo SET Version = 5;
                """, cancellationToken);
        }
        if (schemaVersion < 6)
        {
            await ExecuteAsync(connection, """
                BEGIN IMMEDIATE;
                CREATE TABLE IF NOT EXISTS DocumentClassification (
                    DocumentId TEXT PRIMARY KEY,
                    TagsJson TEXT NOT NULL,
                    Type TEXT NULL,
                    AliasesJson TEXT NOT NULL,
                    Revision INTEGER NOT NULL
                );
                UPDATE SchemaInfo SET Version = 6;
                COMMIT;
                """, cancellationToken);
        }
        if (schemaVersion < 7)
        {
            await ExecuteAsync(connection, """
                BEGIN IMMEDIATE;
                CREATE TABLE IF NOT EXISTS DocumentRelationships (
                    RelationshipId TEXT PRIMARY KEY,
                    SourceId TEXT NOT NULL,
                    TargetId TEXT NOT NULL,
                    Kind TEXT NOT NULL,
                    KindKey TEXT NOT NULL,
                    Note TEXT NULL,
                    Revision INTEGER NOT NULL,
                    UNIQUE(SourceId, TargetId, KindKey),
                    CHECK(SourceId <> TargetId)
                );
                UPDATE SchemaInfo SET Version = 7;
                COMMIT;
                """, cancellationToken);
        }
        if (schemaVersion < 8)
        {
            await ExecuteAsync(connection, """
                BEGIN IMMEDIATE;
                CREATE TABLE IF NOT EXISTS DeletedDocuments (
                    OperationId TEXT PRIMARY KEY,
                    IntentJson TEXT NOT NULL,
                    Content BLOB NOT NULL,
                    CapturedUtc TEXT NOT NULL,
                    Status TEXT NOT NULL,
                    PinnedUtc TEXT NULL,
                    MetadataSnapshot TEXT NOT NULL
                );
                UPDATE SchemaInfo SET Version = 8;
                COMMIT;
                """, cancellationToken);
        }
        await InitializePathPolicyAsync(connection, schemaVersion, cancellationToken);
        _log.Information("database.initialized", paths.DatabasePath);
    }

    public async Task<AppearancePreference> GetAppearanceAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT SettingValue FROM AppSettings WHERE SettingKey='Appearance' LIMIT 1;";
        var value = await command.ExecuteScalarAsync(cancellationToken) as string;
        return Enum.TryParse<AppearancePreference>(value, ignoreCase: true, out var preference) && Enum.IsDefined(preference)
            ? preference
            : AppearancePreference.System;
    }

    public async Task SetAppearanceAsync(AppearancePreference preference, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(preference)) preference = AppearancePreference.System;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AppSettings(SettingKey, SettingValue) VALUES('Appearance', $value)
            ON CONFLICT(SettingKey) DO UPDATE SET SettingValue=excluded.SettingValue;
            """;
        command.Parameters.AddWithValue("$value", preference.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<WorkspaceRecord> OpenWorkspaceAsync(
        string normalizedRootPath,
        string displayName,
        DateTimeOffset openedUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        var rootPath = WorkspaceService.NormalizeRootPath(normalizedRootPath);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var workspaceId = Guid.NewGuid();
        await using (var upsert = connection.CreateCommand())
        {
            upsert.Transaction = transaction;
            upsert.CommandText = """
                INSERT INTO Workspaces(WorkspaceId, RootPath, DisplayName, CreatedUtc, LastOpenedUtc)
                VALUES($id, $root, $name, $created, $opened)
                ON CONFLICT(RootPath) DO UPDATE SET
                    RootPath=excluded.RootPath,
                    DisplayName=excluded.DisplayName,
                    LastOpenedUtc=excluded.LastOpenedUtc;
                """;
            upsert.Parameters.AddWithValue("$id", workspaceId.ToString("D"));
            upsert.Parameters.AddWithValue("$root", rootPath);
            upsert.Parameters.AddWithValue("$name", displayName);
            upsert.Parameters.AddWithValue("$created", openedUtc.ToString("O"));
            upsert.Parameters.AddWithValue("$opened", openedUtc.ToString("O"));
            await upsert.ExecuteNonQueryAsync(cancellationToken);
        }

        WorkspaceRecord workspace;
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = $"""
                SELECT WorkspaceId, RootPath, DisplayName, CreatedUtc, LastOpenedUtc
                FROM Workspaces
                WHERE RootPath = $root COLLATE {PathCollation};
                """;
            select.Parameters.AddWithValue("$root", rootPath);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("The workspace record could not be read after it was saved.");
            workspace = ReadWorkspace(reader);
        }

        await using (var activate = connection.CreateCommand())
        {
            activate.Transaction = transaction;
            activate.CommandText = """
                INSERT INTO ActiveWorkspace(SingletonId, WorkspaceId) VALUES(1, $id)
                ON CONFLICT(SingletonId) DO UPDATE SET WorkspaceId=excluded.WorkspaceId;
                """;
            activate.Parameters.AddWithValue("$id", workspace.WorkspaceId.ToString("D"));
            await activate.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        _log.Information("workspace.opened", workspace.RootPath);
        return workspace;
    }

    public async Task<WorkspaceRecord?> GetActiveWorkspaceAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT w.WorkspaceId, w.RootPath, w.DisplayName, w.CreatedUtc, w.LastOpenedUtc
            FROM ActiveWorkspace a
            INNER JOIN Workspaces w ON w.WorkspaceId = a.WorkspaceId
            WHERE a.SingletonId = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadWorkspace(reader) : null;
    }

    public async Task<IReadOnlyList<WorkspacePin>> GetPinsAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT RelativePath, PinnedUtc FROM WorkspacePins WHERE WorkspaceId=$id ORDER BY RelativePath COLLATE NOCASE;";
        command.Parameters.AddWithValue("$id", workspaceId.ToString("D"));
        var pins = new List<WorkspacePin>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            pins.Add(new WorkspacePin(reader.GetString(0), DateTimeOffset.Parse(reader.GetString(1))));
        return pins;
    }

    public async Task AddPinAsync(Guid workspaceId, string relativePath, DateTimeOffset pinnedUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO WorkspacePins(WorkspaceId, RelativePath, PinnedUtc) VALUES($id, $path, $pinned)
            ON CONFLICT(WorkspaceId, RelativePath) DO UPDATE SET PinnedUtc=excluded.PinnedUtc;
            """;
        command.Parameters.AddWithValue("$id", workspaceId.ToString("D"));
        command.Parameters.AddWithValue("$path", relativePath);
        command.Parameters.AddWithValue("$pinned", pinnedUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RemovePinAsync(Guid workspaceId, string relativePath, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM WorkspacePins WHERE WorkspaceId=$id AND RelativePath=$path COLLATE {PathCollation};";
        command.Parameters.AddWithValue("$id", workspaceId.ToString("D"));
        command.Parameters.AddWithValue("$path", relativePath);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<string> BeginSessionAsync(CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid().ToString("N");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO AppSessions(SessionId, StartedUtc, CleanExit) VALUES ($id, $started, 0);";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$started", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return id;
    }

    public async Task EndSessionAsync(string sessionId, bool cleanExit, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE AppSessions SET EndedUtc=$ended, CleanExit=$clean WHERE SessionId=$id;";
        command.Parameters.AddWithValue("$ended", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$clean", cleanExit ? 1 : 0);
        command.Parameters.AddWithValue("$id", sessionId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveDraftAsync(RecoveryDraft draft, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RecoveryDrafts(DraftKey, FilePath, TextContent, UpdatedUtc, SavedFileWriteUtc, IsNewDocument)
            VALUES($key, $path, $text, $updated, $saved, $isNew)
            ON CONFLICT(DraftKey) DO UPDATE SET
                FilePath=excluded.FilePath, TextContent=excluded.TextContent,
                UpdatedUtc=excluded.UpdatedUtc, SavedFileWriteUtc=excluded.SavedFileWriteUtc,
                IsNewDocument=excluded.IsNewDocument;
            """;
        command.Parameters.AddWithValue("$key", draft.DraftKey);
        command.Parameters.AddWithValue("$path", (object?)draft.FilePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$text", draft.Text);
        command.Parameters.AddWithValue("$updated", draft.UpdatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$saved", draft.SavedFileWriteUtc?.ToString("O") is { } saved ? saved : DBNull.Value);
        command.Parameters.AddWithValue("$isNew", draft.IsNewDocument ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken);
        _log.Information("recovery.saved", draft.FilePath);
    }

    public async Task DeleteDraftAsync(string draftKey, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM RecoveryDrafts WHERE DraftKey=$key;";
        command.Parameters.AddWithValue("$key", draftKey);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RecoveryDraft>> GetRecoverableDraftsAsync(CancellationToken cancellationToken = default)
    {
        var drafts = new List<RecoveryDraft>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DraftKey, FilePath, TextContent, UpdatedUtc, SavedFileWriteUtc, IsNewDocument FROM RecoveryDrafts ORDER BY UpdatedUtc DESC;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            drafts.Add(new RecoveryDraft(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3)),
                reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4)),
                reader.GetInt64(5) != 0));
        }
        return drafts;
    }

    public async Task RecordIdentityAsync(Guid documentId, string path, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO DocumentMetadata(DocumentId, FilePath, LastSeenUtc) VALUES($id, $path, $seen)
            ON CONFLICT(DocumentId, FilePath) DO UPDATE SET LastSeenUtc=excluded.LastSeenUtc;
            """;
        command.Parameters.AddWithValue("$id", documentId.ToString("D"));
        command.Parameters.AddWithValue("$path", Path.GetFullPath(path));
        command.Parameters.AddWithValue("$seen", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RemovePathAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM DocumentMetadata WHERE FilePath=$path;";
        command.Parameters.AddWithValue("$path", Path.GetFullPath(path));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DocumentIdentityOccurrence>> GetIdentitiesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<DocumentIdentityOccurrence>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DocumentId, FilePath FROM DocumentMetadata ORDER BY DocumentId, FilePath;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new DocumentIdentityOccurrence(Guid.Parse(reader.GetString(0)), reader.GetString(1)));
        return result;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await ExecuteAsync(connection, "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;", cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> GetSchemaVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Version FROM SchemaInfo LIMIT 1;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static WorkspaceRecord ReadWorkspace(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        reader.GetString(1),
        reader.GetString(2),
        DateTimeOffset.Parse(reader.GetString(3)),
        DateTimeOffset.Parse(reader.GetString(4)));

    private void BackupExistingDatabase()
    {
        if (!File.Exists(paths.DatabasePath)) return;
        Directory.CreateDirectory(paths.BackupDirectory);
        var backup = Path.Combine(paths.BackupDirectory, $"axiom-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}.db");
        StateMaintenanceService.BackupDatabase(paths.DatabasePath, backup);
        foreach (var stale in Directory.EnumerateFiles(paths.BackupDirectory, "axiom-*.db")
                     .OrderByDescending(File.GetLastWriteTimeUtc).Skip(10))
            File.Delete(stale);
    }
}
