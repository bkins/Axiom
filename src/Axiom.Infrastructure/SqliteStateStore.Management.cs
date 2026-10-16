using System.Text.Json;
using Axiom.Core;

namespace Axiom.Infrastructure;

public sealed partial class SqliteStateStore
{
    public async Task<IReadOnlyList<WorkspaceRecord>> ListWorkspacesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<WorkspaceRecord>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT WorkspaceId,RootPath,DisplayName,CreatedUtc,LastOpenedUtc FROM Workspaces ORDER BY LastOpenedUtc DESC;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadWorkspace(reader));
        return result;
    }

    public async Task ForgetWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM Workspaces WHERE WorkspaceId=$id; DELETE FROM AppSettings WHERE SettingKey IN ($sessionKey,$indexKey);";
        command.Parameters.AddWithValue("$id", workspaceId.ToString("D"));
        command.Parameters.AddWithValue("$sessionKey", SessionSettingKey(workspaceId));
        command.Parameters.AddWithValue("$indexKey", $"LinkIndex:{workspaceId:D}");
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task PrepareMutationAsync(DocumentMutationIntent intent, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO DocumentMutations(OperationId,IntentJson) VALUES($id,$json);";
        command.Parameters.AddWithValue("$id", intent.OperationId.ToString("D"));
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(intent));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DocumentMutationIntent>> GetPendingMutationsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<DocumentMutationIntent>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT IntentJson FROM DocumentMutations;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(JsonSerializer.Deserialize<DocumentMutationIntent>(reader.GetString(0))!);
        return result;
    }

    public async Task ReconcileMutationAsync(DocumentMutationIntent intent, bool applied, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$operation", intent.OperationId.ToString("D"));
        command.Parameters.AddWithValue("$workspace", intent.WorkspaceId.ToString("D"));
        command.Parameters.AddWithValue("$source", (object?)intent.SourcePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$destination", (object?)intent.DestinationPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$oldRelative", intent.SourcePath is null ? "" : Path.GetRelativePath(intent.RootPath, intent.SourcePath));
        command.Parameters.AddWithValue("$newRelative", intent.DestinationPath is null ? "" : Path.GetRelativePath(intent.RootPath, intent.DestinationPath));
        command.Parameters.AddWithValue("$identity", intent.DocumentId?.ToString("D") ?? "");
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        if (applied)
        {
            if (intent.Kind == DocumentMutationKind.CopyFolder)
            {
                foreach (var path in (await new FileDocumentStore().ListMarkdownFilesAsync(intent.DestinationPath!, cancellationToken)).Files)
                {
                    if (DocumentIdCodec.Find(MarkdownIdentityBytes.Decode(await File.ReadAllBytesAsync(path, cancellationToken))) is not { } copiedId) continue;
                    await using var insert = connection.CreateCommand(); insert.Transaction = transaction;
                    insert.CommandText = "INSERT OR IGNORE INTO DocumentMetadata(DocumentId,FilePath,LastSeenUtc) VALUES($id,$path,$now);";
                    insert.Parameters.AddWithValue("$id", copiedId.ToString("D")); insert.Parameters.AddWithValue("$path", path); insert.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
                    await insert.ExecuteNonQueryAsync(cancellationToken);
                }
            }
            if (intent.Kind is DocumentMutationKind.MoveFolder or DocumentMutationKind.Move or DocumentMutationKind.Rename or DocumentMutationKind.ExternalMove)
                await RebindFolderStateAsync(connection, transaction, intent, cancellationToken);
            if (intent.Kind is DocumentMutationKind.Move or DocumentMutationKind.Rename or DocumentMutationKind.Recycle or DocumentMutationKind.ExternalMove)
            {
                command.CommandText = "DELETE FROM DocumentMetadata WHERE FilePath=$source;";
                if (intent.Kind == DocumentMutationKind.Recycle)
                    command.CommandText += "DELETE FROM WorkspacePins WHERE WorkspaceId=$workspace AND RelativePath=$oldRelative;";
                else
                    command.CommandText += """
                        INSERT OR IGNORE INTO WorkspacePins(WorkspaceId,RelativePath,PinnedUtc)
                        SELECT WorkspaceId,$newRelative,PinnedUtc FROM WorkspacePins WHERE WorkspaceId=$workspace AND RelativePath=$oldRelative;
                        DELETE FROM WorkspacePins WHERE WorkspaceId=$workspace AND RelativePath=$oldRelative;
                        """;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            if (intent.DocumentId is not null && intent.DestinationPath is not null)
            {
                command.CommandText = """
                    INSERT INTO DocumentMetadata(DocumentId,FilePath,LastSeenUtc) VALUES($identity,$destination,$now)
                    ON CONFLICT(DocumentId,FilePath) DO UPDATE SET LastSeenUtc=excluded.LastSeenUtc;
                    """;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        if (intent.Kind == DocumentMutationKind.Recycle)
        {
            command.CommandText = "UPDATE DeletedDocuments SET Status=$status WHERE OperationId=$operation;";
            command.Parameters.AddWithValue("$status", applied ? "Available" : "Cancelled");
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        if (intent.Kind == DocumentMutationKind.Restore && intent.RecoveryOperationId is { } recovery)
        {
            command.Parameters.AddWithValue("$recovery", recovery.ToString("D"));
            if (applied)
            {
                command.CommandText = """
                    INSERT OR IGNORE INTO WorkspacePins(WorkspaceId,RelativePath,PinnedUtc)
                    SELECT $workspace,$newRelative,PinnedUtc FROM DeletedDocuments WHERE OperationId=$recovery AND PinnedUtc IS NOT NULL;
                    """;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            command.CommandText = "UPDATE DeletedDocuments SET Status=$restoreStatus WHERE OperationId=$recovery;";
            command.Parameters.AddWithValue("$restoreStatus", applied ? "Restored" : "Available");
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        command.CommandText = "DELETE FROM DocumentMutations WHERE OperationId=$operation;";
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
