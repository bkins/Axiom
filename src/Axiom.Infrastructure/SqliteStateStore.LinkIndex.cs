using System.Text.Json;
using Axiom.Core;

namespace Axiom.Infrastructure;

public sealed partial class SqliteStateStore : IWorkspaceLinkIndexStore
{
    public Task SaveLinkIndexAsync(Guid workspaceId, WorkspaceBacklinkIndex index, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        if (workspaceId == Guid.Empty || index.FormatVersion != 1) throw new ArgumentException("A workspace and supported index format are required.");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO AppSettings(SettingKey,SettingValue) VALUES($key,$value) ON CONFLICT(SettingKey) DO UPDATE SET SettingValue=excluded.SettingValue;";
        command.Parameters.AddWithValue("$key", $"LinkIndex:{workspaceId:D}");
        command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(index));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }, cancellationToken);

    public Task<WorkspaceBacklinkIndex?> GetLinkIndexAsync(Guid workspaceId, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT SettingValue FROM AppSettings WHERE SettingKey=$key;";
        command.Parameters.AddWithValue("$key", $"LinkIndex:{workspaceId:D}");
        if (await command.ExecuteScalarAsync(cancellationToken) is not string json) return null;
        var index = JsonSerializer.Deserialize<WorkspaceBacklinkIndex>(json);
        if (index?.FormatVersion != 1 || index.Documents is null || index.Errors is null)
            throw new InvalidDataException("The saved link index has an unsupported format; rebuild it from Markdown.");
        return index;
    }, cancellationToken);
}
