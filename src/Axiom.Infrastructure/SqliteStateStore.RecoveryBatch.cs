using Axiom.Core;

namespace Axiom.Infrastructure;

public sealed partial class SqliteStateStore : IBatchRecoveryStore
{
    public Task ApplyDraftsAsync(
        IReadOnlyList<RecoveryDraft> drafts,
        IReadOnlyList<string> deletedKeys,
        CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        foreach (var draft in drafts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
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
        }
        foreach (var key in deletedKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM RecoveryDrafts WHERE DraftKey=$key;";
            command.Parameters.AddWithValue("$key", key);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(cancellationToken);
        foreach (var draft in drafts) _log.Information("recovery.saved", draft.FilePath);
    }, cancellationToken);
}
