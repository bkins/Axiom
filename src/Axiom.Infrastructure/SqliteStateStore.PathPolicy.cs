using Microsoft.Data.Sqlite;

namespace Axiom.Infrastructure;

public sealed partial class SqliteStateStore
{
    private async Task InitializePathPolicyAsync(SqliteConnection connection, int originalVersion, CancellationToken cancellationToken)
    {
        await using var policy = connection.CreateCommand();
        policy.CommandText = "SELECT SettingValue FROM AppSettings WHERE SettingKey='FilesystemPathPolicy';";
        var stored = await policy.ExecuteScalarAsync(cancellationToken) as string;
        if (stored is not null && stored != PathCollation)
            throw new InvalidOperationException("State path policy differs from this platform. Preserve this state and choose a separate state root; cross-policy state migration requires explicit review.");
        if (originalVersion >= 9)
        {
            if (stored != PathCollation) throw new InvalidOperationException("State path policy differs from this platform. Preserve this state and choose a separate state root; cross-policy state migration requires explicit review.");
            return;
        }
        // Preserve all schema-8 rows. Binary identity prevents Unix case variants from merging.
        // Existing Windows NOCASE behavior remains unchanged. No missing historical observations are invented.
        if (CaseSensitivePaths)
        {
            await ExecuteAsync(connection, "PRAGMA foreign_keys=OFF;", cancellationToken);
            try
            {
                await using var transaction = connection.BeginTransaction(deferred: false);
                await using var migration = connection.CreateCommand();
                migration.Transaction = transaction;
                migration.CommandText = """
                    CREATE TABLE Workspaces_PathMigration (
                        WorkspaceId TEXT PRIMARY KEY, RootPath TEXT NOT NULL COLLATE BINARY UNIQUE,
                        DisplayName TEXT NOT NULL, CreatedUtc TEXT NOT NULL, LastOpenedUtc TEXT NOT NULL);
                    INSERT INTO Workspaces_PathMigration SELECT * FROM Workspaces;
                    CREATE TABLE WorkspacePins_PathMigration (
                        WorkspaceId TEXT NOT NULL, RelativePath TEXT NOT NULL COLLATE BINARY, PinnedUtc TEXT NOT NULL,
                        PRIMARY KEY(WorkspaceId,RelativePath),
                        FOREIGN KEY(WorkspaceId) REFERENCES Workspaces_PathMigration(WorkspaceId) ON DELETE CASCADE);
                    INSERT INTO WorkspacePins_PathMigration SELECT * FROM WorkspacePins;
                    CREATE TABLE DocumentMetadata_PathMigration (
                        DocumentId TEXT NOT NULL, FilePath TEXT NOT NULL COLLATE BINARY, LastSeenUtc TEXT NOT NULL,
                        PRIMARY KEY(DocumentId,FilePath));
                    INSERT INTO DocumentMetadata_PathMigration SELECT * FROM DocumentMetadata;
                    DROP TABLE WorkspacePins;
                    DROP TABLE Workspaces;
                    DROP TABLE DocumentMetadata;
                    ALTER TABLE Workspaces_PathMigration RENAME TO Workspaces;
                    ALTER TABLE WorkspacePins_PathMigration RENAME TO WorkspacePins;
                    ALTER TABLE DocumentMetadata_PathMigration RENAME TO DocumentMetadata;
                    CREATE INDEX IX_DocumentMetadata_Path ON DocumentMetadata(FilePath);
                    """;
                await migration.ExecuteNonQueryAsync(cancellationToken);
                migration.CommandText = "UPDATE SchemaInfo SET Version=9; INSERT INTO AppSettings(SettingKey,SettingValue) VALUES('FilesystemPathPolicy','BINARY') ON CONFLICT(SettingKey) DO UPDATE SET SettingValue=excluded.SettingValue;";
                await migration.ExecuteNonQueryAsync(cancellationToken);
                await VerifyPathForeignKeysAsync(connection, transaction, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            finally { await ExecuteAsync(connection, "PRAGMA foreign_keys=ON;", CancellationToken.None); }
        }
        else
        {
            await using var transaction = connection.BeginTransaction(deferred: false);
            await using var migration = connection.CreateCommand();
            migration.Transaction = transaction;
            migration.CommandText = "UPDATE SchemaInfo SET Version=9; INSERT INTO AppSettings(SettingKey,SettingValue) VALUES('FilesystemPathPolicy','NOCASE') ON CONFLICT(SettingKey) DO UPDATE SET SettingValue=excluded.SettingValue;";
            await migration.ExecuteNonQueryAsync(cancellationToken);
            await VerifyPathForeignKeysAsync(connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private static async Task VerifyPathForeignKeysAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await using var integrity = connection.CreateCommand();
        integrity.Transaction = transaction;
        integrity.CommandText = "PRAGMA foreign_key_check;";
        if (await integrity.ExecuteScalarAsync(cancellationToken) is not null) throw new InvalidOperationException("State path migration failed foreign-key verification; preserve the backup for recovery.");
    }
}
