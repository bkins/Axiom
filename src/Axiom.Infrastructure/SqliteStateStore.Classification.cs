using System.Text.Json;
using Axiom.Core;
using Microsoft.Data.Sqlite;

namespace Axiom.Infrastructure;

public sealed partial class SqliteStateStore : IClassificationStore
{
    public Task<IReadOnlyList<DocumentClassification>> GetClassificationsAsync(CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DocumentId, TagsJson, Type, AliasesJson, Revision FROM DocumentClassification ORDER BY DocumentId;";
        var values = new List<DocumentClassification>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            values.Add(new(Guid.Parse(reader.GetString(0)), JsonSerializer.Deserialize<string[]>(reader.GetString(1))!, reader.IsDBNull(2) ? null : reader.GetString(2), JsonSerializer.Deserialize<string[]>(reader.GetString(3))!, reader.GetInt64(4)));
        return (IReadOnlyList<DocumentClassification>)values;
    }, cancellationToken);

    public Task ApplyClassificationsAsync(IReadOnlyList<ClassificationChange> changes, CancellationToken cancellationToken = default) =>
        ApplyConnectedMetadataAsync(changes, [], cancellationToken);

    private static async Task WriteClassificationsAsync(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<ClassificationChange> changes, CancellationToken cancellationToken)
    {
        if (changes.Select(change => change.Value.DocumentId).Distinct().Count() != changes.Count) throw new ArgumentException("Duplicate document IDs.");
        var normalized = changes.Select(change => change with { Value = ClassificationPolicy.Normalize(change.Value) }).ToArray();
        foreach (var change in normalized)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var read = connection.CreateCommand();
            read.Transaction = transaction;
            read.CommandText = "SELECT Revision FROM DocumentClassification WHERE DocumentId=$id;";
            read.Parameters.AddWithValue("$id", change.Value.DocumentId.ToString("D"));
            var revision = Convert.ToInt64(await read.ExecuteScalarAsync(cancellationToken) ?? 0L);
            if (revision != change.ExpectedRevision) throw new InvalidOperationException("Metadata changed since review. Refresh before applying.");
            await using var write = connection.CreateCommand();
            write.Transaction = transaction;
            write.CommandText = """
                INSERT INTO DocumentClassification(DocumentId, TagsJson, Type, AliasesJson, Revision) VALUES($id,$tags,$type,$aliases,$revision)
                ON CONFLICT(DocumentId) DO UPDATE SET TagsJson=excluded.TagsJson, Type=excluded.Type, AliasesJson=excluded.AliasesJson, Revision=excluded.Revision;
                """;
            write.Parameters.AddWithValue("$id", change.Value.DocumentId.ToString("D"));
            write.Parameters.AddWithValue("$tags", JsonSerializer.Serialize(change.Value.Tags));
            write.Parameters.AddWithValue("$type", (object?)change.Value.Type ?? DBNull.Value);
            write.Parameters.AddWithValue("$aliases", JsonSerializer.Serialize(change.Value.Aliases));
            write.Parameters.AddWithValue("$revision", checked(revision + 1));
            await write.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
