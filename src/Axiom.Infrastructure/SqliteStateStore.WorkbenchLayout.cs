using System.Text.Json;
using Axiom.Core;

namespace Axiom.Infrastructure;

public sealed partial class SqliteStateStore : IWorkbenchLayoutStore
{
    public async Task<WorkbenchLayout> GetWorkbenchLayoutAsync(CancellationToken cancellationToken = default)
    {
        var json = await ReadSettingAsync("WorkbenchLayout", cancellationToken);
        try
        {
            var layout = json is null ? new WorkbenchLayout() : JsonSerializer.Deserialize<WorkbenchLayout>(json);
            return layout is { IsValid: true } ? layout : new();
        }
        catch (JsonException) { return new(); }
    }

    public async Task SetWorkbenchLayoutAsync(WorkbenchLayout layout, CancellationToken cancellationToken = default)
    {
        if (!layout.IsValid) throw new ArgumentException("Workbench panel dimensions or selected view are invalid.", nameof(layout));
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        await WriteSettingAsync(connection, transaction, "WorkbenchLayout", JsonSerializer.Serialize(layout), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
