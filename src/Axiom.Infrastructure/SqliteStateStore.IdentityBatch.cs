using Axiom.Core;

namespace Axiom.Infrastructure;

public sealed partial class SqliteStateStore : IBatchIdentityStore
{
    public Task RecordIdentitiesAsync(IReadOnlyList<DocumentIdentityOccurrence> identities, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO DocumentMetadata(DocumentId, FilePath, LastSeenUtc) VALUES($id,$path,$seen)
            ON CONFLICT(DocumentId,FilePath) DO UPDATE SET LastSeenUtc=excluded.LastSeenUtc;
            """;
        var id = command.Parameters.Add("$id", Microsoft.Data.Sqlite.SqliteType.Text);
        var path = command.Parameters.Add("$path", Microsoft.Data.Sqlite.SqliteType.Text);
        command.Parameters.AddWithValue("$seen", DateTimeOffset.UtcNow.ToString("O"));
        foreach (var identity in identities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            id.Value = identity.DocumentId.ToString("D");
            path.Value = Path.GetFullPath(identity.Path);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(cancellationToken);
    }, cancellationToken);
}
