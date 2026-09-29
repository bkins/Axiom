using Axiom.Core;

namespace Axiom.Infrastructure;

public sealed partial class SqliteStateStore : IReadingModeStore
{
    public Task<bool> GetReadingModeAsync(CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT SettingValue FROM AppSettings WHERE SettingKey='ReadingMode';";
        return string.Equals(await command.ExecuteScalarAsync(cancellationToken) as string, "true", StringComparison.Ordinal);
    }, cancellationToken);

    public Task SetReadingModeAsync(bool enabled, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO AppSettings VALUES('ReadingMode',$value) ON CONFLICT(SettingKey) DO UPDATE SET SettingValue=excluded.SettingValue;";
        command.Parameters.AddWithValue("$value", enabled ? "true" : "false");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }, cancellationToken);
}
