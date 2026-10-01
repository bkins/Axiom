using System.Text.Json;
using Axiom.Core;

namespace Axiom.Infrastructure;

public sealed partial class SqliteStateStore
{
    public async Task<ShortcutConfiguration> GetShortcutConfigurationAsync(CancellationToken cancellationToken = default)
    {
        var json = await ReadSettingAsync("CommandShortcuts", cancellationToken);
        return json is null ? new() : JsonSerializer.Deserialize<ShortcutConfiguration>(json) ?? throw new IOException("Shortcut configuration is empty.");
    }

    public async Task SetShortcutConfigurationAsync(ShortcutConfiguration configuration
                                                  , IReadOnlyList<AxiomCommand> commands
                                                  , CancellationToken cancellationToken = default)
    {
        configuration.Resolve(commands);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await WriteSettingAsync(connection, transaction, "CommandShortcuts", JsonSerializer.Serialize(configuration), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
