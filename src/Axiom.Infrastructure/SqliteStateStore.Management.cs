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
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Workspaces WHERE WorkspaceId=$id;";
        command.Parameters.AddWithValue("$id", workspaceId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
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
            if (intent.Kind is DocumentMutationKind.Move or DocumentMutationKind.Rename or DocumentMutationKind.Recycle)
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
        command.CommandText = "DELETE FROM DocumentMutations WHERE OperationId=$operation;";
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
