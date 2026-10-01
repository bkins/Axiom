using System.Text.Json;
using Axiom.Core;

namespace Axiom.Infrastructure;

public sealed partial class SqliteStateStore
{
    public async Task<IReadOnlyList<DeletedDocument>> GetDeletedDocumentsAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var result = new List<DeletedDocument>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT IntentJson,Content,CapturedUtc,Status,PinnedUtc,MetadataSnapshot FROM DeletedDocuments ORDER BY CapturedUtc DESC;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var intent = JsonSerializer.Deserialize<DocumentMutationIntent>(reader.GetString(0))!;
            if (intent.WorkspaceId != workspaceId) continue;
            result.Add(new DeletedDocument(intent, (byte[])reader[1], DateTimeOffset.Parse(reader.GetString(2)), reader.GetString(3)
                , reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4)), reader.GetString(5)));
        }
        return result;
    }

    public async Task PrepareDeletionAsync(DocumentMutationIntent intent, byte[] content, CancellationToken cancellationToken = default)
    {
        if (intent.Kind != DocumentMutationKind.Recycle || intent.SourcePath is null || content.Length > 20 * 1024 * 1024)
            throw new IOException("Recovery capture requires a document no larger than 20 MiB. The document was retained.");
        if (WorkspaceFileOperations.Hash(content) != intent.ExpectedHash)
            throw new IOException("Recovery content does not match the reviewed document. The document was retained.");
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO DocumentMutations(OperationId,IntentJson) VALUES($id,$json);
            INSERT INTO DeletedDocuments(OperationId,IntentJson,Content,CapturedUtc,Status,PinnedUtc,MetadataSnapshot)
            VALUES($id,$json,$content,$now,'Prepared',
                (SELECT PinnedUtc FROM WorkspacePins WHERE WorkspaceId=$workspace AND RelativePath=$relative),
                json_object('classification', json((SELECT json_object('DocumentId',DocumentId,'Tags',json(TagsJson),'Type',Type,'Aliases',json(AliasesJson),'Revision',Revision)
                    FROM DocumentClassification WHERE DocumentId=$identity)),
                    'relationships',json((SELECT json_group_array(json_object('RelationshipId',RelationshipId,'SourceId',SourceId,'TargetId',TargetId,'Kind',Kind,'Note',Note,'Revision',Revision))
                    FROM DocumentRelationships WHERE SourceId=$identity OR TargetId=$identity))));
            """;
        command.Parameters.AddWithValue("$id", intent.OperationId.ToString("D"));
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(intent));
        command.Parameters.AddWithValue("$content", content);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$workspace", intent.WorkspaceId.ToString("D"));
        command.Parameters.AddWithValue("$relative", Path.GetRelativePath(intent.RootPath, intent.SourcePath));
        command.Parameters.AddWithValue("$identity", intent.DocumentId?.ToString("D") ?? "");
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task PrepareRestoreAsync(DocumentMutationIntent intent, CancellationToken cancellationToken = default)
    {
        if (intent.Kind != DocumentMutationKind.Restore || intent.RecoveryOperationId is null || intent.DestinationPath is null)
            throw new ArgumentException("Restore requires a recovery item and a new destination.");
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE DeletedDocuments SET Status='Restoring' WHERE OperationId=$recovery AND Status='Available';";
        command.Parameters.AddWithValue("$recovery", intent.RecoveryOperationId.Value.ToString("D"));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new IOException("This recovery item is unavailable or already being restored.");
        command.CommandText = "INSERT INTO DocumentMutations(OperationId,IntentJson) VALUES($id,$json);";
        command.Parameters.AddWithValue("$id", intent.OperationId.ToString("D"));
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(intent));
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
