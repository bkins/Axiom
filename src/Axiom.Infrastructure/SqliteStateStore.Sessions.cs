using System.Text.Json;
using Axiom.Core;

namespace Axiom.Infrastructure;

public sealed partial class SqliteStateStore : IWorkspaceSessionStore
{
    public async Task SaveWorkspaceSessionAsync(Guid workspaceId, WorkspaceDocumentSession session, CancellationToken cancellationToken = default)
    {
        if (workspaceId == Guid.Empty || session.FormatVersion != 1)
            throw new ArgumentException("A workspace and supported session format are required.");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AppSettings(SettingKey, SettingValue) VALUES($key, $value)
            ON CONFLICT(SettingKey) DO UPDATE SET SettingValue=excluded.SettingValue;
            """;
        command.Parameters.AddWithValue("$key", SessionSettingKey(workspaceId));
        command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(session));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<WorkspaceDocumentSession?> GetWorkspaceSessionAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT SettingValue FROM AppSettings WHERE SettingKey=$key;";
        command.Parameters.AddWithValue("$key", SessionSettingKey(workspaceId));
        if (await command.ExecuteScalarAsync(cancellationToken) is not string json) return null;
        var session = JsonSerializer.Deserialize<WorkspaceDocumentSession>(json);
        if (session?.FormatVersion != 1 || session.Documents is null)
            throw new InvalidDataException("The remembered open-document session has an unsupported format.");
        return session;
    }

    private static string SessionSettingKey(Guid workspaceId) => $"WorkspaceSession:{workspaceId:D}";
}
