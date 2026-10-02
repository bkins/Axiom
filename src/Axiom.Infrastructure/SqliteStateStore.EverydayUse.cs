using System.Text.Json;
using Axiom.Core;
using Microsoft.Data.Sqlite;

namespace Axiom.Infrastructure;

public sealed partial class SqliteStateStore
{
    public async Task<EditorPreferences> GetEditorPreferencesAsync(CancellationToken cancellationToken = default)
    {
        var json = await ReadSettingAsync("EditorPreferences", cancellationToken);
        try
        {
            var value = json is null ? new EditorPreferences() : JsonSerializer.Deserialize<EditorPreferences>(json) ?? new();
            value.Validate();
            return value;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException) { return new(); }
    }

    public async Task SetEditorPreferencesAsync(EditorPreferences preferences, AppearancePreference appearance, CancellationToken cancellationToken = default)
    {
        preferences.Validate();
        if (!Enum.IsDefined(appearance)) throw new ArgumentException("Choose System, Light or Dark appearance.");
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await WriteSettingAsync(connection, transaction, "EditorPreferences", JsonSerializer.Serialize(preferences), cancellationToken);
        await WriteSettingAsync(connection, transaction, "Appearance", appearance.ToString(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RecentDocument>> GetRecentDocumentsAsync(CancellationToken cancellationToken = default)
    {
        var json = await ReadSettingAsync("RecentDocuments", cancellationToken);
        try { return json is null ? [] : (JsonSerializer.Deserialize<RecentDocument[]>(json) ?? []).Take(20).ToArray(); }
        catch (JsonException) { return []; }
    }

    public async Task RecordRecentDocumentAsync(string path, string? workspaceRoot, CancellationToken cancellationToken = default)
    {
        path = System.IO.Path.GetFullPath(path);
        await MutateRecentsAsync(values => values.Where(value => !value.Path.Equals(path, PathComparison))
            .Prepend(new RecentDocument(path, workspaceRoot, DateTimeOffset.UtcNow)).Take(20).ToArray(), cancellationToken);
    }

    public Task RemoveRecentDocumentAsync(string? path, CancellationToken cancellationToken = default) =>
        MutateRecentsAsync(values => path is null ? [] : values.Where(value => !value.Path.Equals(path, PathComparison)).ToArray(), cancellationToken);

    private async Task MutateRecentsAsync(Func<RecentDocument[], RecentDocument[]> change, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        await using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = "SELECT SettingValue FROM AppSettings WHERE SettingKey='RecentDocuments';";
        var json = await read.ExecuteScalarAsync(cancellationToken) as string;
        RecentDocument[] values;
        try { values = json is null ? [] : JsonSerializer.Deserialize<RecentDocument[]>(json) ?? []; }
        catch (JsonException) { values = []; }
        await WriteSettingAsync(connection, transaction, "RecentDocuments", JsonSerializer.Serialize(change(values)), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<string?> ReadSettingAsync(string key, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT SettingValue FROM AppSettings WHERE SettingKey=$key;";
        command.Parameters.AddWithValue("$key", key);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private static async Task WriteSettingAsync(SqliteConnection connection, SqliteTransaction transaction, string key, string value, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO AppSettings VALUES($key,$value) ON CONFLICT(SettingKey) DO UPDATE SET SettingValue=excluded.SettingValue;";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
