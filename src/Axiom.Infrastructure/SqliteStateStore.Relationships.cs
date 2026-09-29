using Axiom.Core;
using Microsoft.Data.Sqlite;

namespace Axiom.Infrastructure;

public sealed partial class SqliteStateStore : IConnectedMetadataStore
{
    public Task<IReadOnlyList<DocumentRelationship>> GetRelationshipsAsync(CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT RelationshipId, SourceId, TargetId, Kind, Note, Revision FROM DocumentRelationships ORDER BY SourceId, TargetId, KindKey;";
        var values = new List<DocumentRelationship>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            values.Add(new(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), Guid.Parse(reader.GetString(2)), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetInt64(5)));
        return (IReadOnlyList<DocumentRelationship>)values;
    }, cancellationToken);

    public Task ApplyConnectedMetadataAsync(IReadOnlyList<ClassificationChange> classifications, IReadOnlyList<RelationshipChange> relationships, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        if (relationships.Select(change => change.Value.RelationshipId).Distinct().Count() != relationships.Count) throw new ArgumentException("Duplicate relationship IDs.");
        var normalized = relationships.Select(change => change with { Value = RelationshipPolicy.Normalize(change.Value) }).ToArray();
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        await WriteClassificationsAsync(connection, transaction, classifications, cancellationToken);
        foreach (var change in normalized)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var read = connection.CreateCommand();
            read.Transaction = transaction;
            read.CommandText = "SELECT Revision FROM DocumentRelationships WHERE RelationshipId=$id;";
            read.Parameters.AddWithValue("$id", change.Value.RelationshipId.ToString("D"));
            var revision = Convert.ToInt64(await read.ExecuteScalarAsync(cancellationToken) ?? 0L);
            if (revision != change.ExpectedRevision || change.Delete && revision == 0)
                throw new InvalidOperationException("Relationship changed since review. Refresh before applying.");
            await using var write = connection.CreateCommand();
            write.Transaction = transaction;
            write.Parameters.AddWithValue("$id", change.Value.RelationshipId.ToString("D"));
            if (change.Delete) write.CommandText = "DELETE FROM DocumentRelationships WHERE RelationshipId=$id;";
            else
            {
                write.CommandText = """
                    INSERT INTO DocumentRelationships(RelationshipId,SourceId,TargetId,Kind,KindKey,Note,Revision) VALUES($id,$source,$target,$kind,$key,$note,$revision)
                    ON CONFLICT(RelationshipId) DO UPDATE SET SourceId=excluded.SourceId, TargetId=excluded.TargetId, Kind=excluded.Kind, KindKey=excluded.KindKey, Note=excluded.Note, Revision=excluded.Revision;
                    """;
                write.Parameters.AddWithValue("$source", change.Value.SourceId.ToString("D"));
                write.Parameters.AddWithValue("$target", change.Value.TargetId.ToString("D"));
                write.Parameters.AddWithValue("$kind", change.Value.Kind);
                write.Parameters.AddWithValue("$key", change.Value.Kind.ToUpperInvariant());
                write.Parameters.AddWithValue("$note", (object?)change.Value.Note ?? DBNull.Value);
                write.Parameters.AddWithValue("$revision", checked(revision + 1));
            }
            try { await write.ExecuteNonQueryAsync(cancellationToken); }
            catch (SqliteException exception) when (exception.SqliteErrorCode == 19) { throw new InvalidOperationException("This directed relationship and kind already exist.", exception); }
        }
        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(cancellationToken);
    }, cancellationToken);
}
