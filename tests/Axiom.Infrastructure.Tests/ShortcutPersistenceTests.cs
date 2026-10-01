using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.Infrastructure.Tests;

public sealed class ShortcutPersistenceTests
{
    [Fact]
    public async Task RestartRetainsBindingsAndRejectedConflictDoesNotReplaceSettings()
    {
        using var fixture = new MutationTestWorkspace();
        var commands = new[]
        {
            new AxiomCommand("save", "Save", "Ctrl+S", false, _ => CommandAvailability.Available,
                (_, _) => Task.FromResult(new CommandResult(CommandOutcome.Completed, "Saved")))
          , new AxiomCommand("new", "New", "Ctrl+N", false, _ => CommandAvailability.Available,
                (_, _) => Task.FromResult(new CommandResult(CommandOutcome.Completed, "Opened")))
        };
        Assert.Empty((await fixture.State.GetShortcutConfigurationAsync()).Overrides);
        await fixture.State.SetShortcutConfigurationAsync(new() { Overrides = new() { ["save"] = "Ctrl+J", ["new"] = null } }, commands);
        var reopened = new SqliteStateStore(fixture.State.Paths);
        var retained = await reopened.GetShortcutConfigurationAsync();
        Assert.Equal("Ctrl+J", retained.Resolve(commands)["save"]);
        Assert.Null(retained.Resolve(commands)["new"]);
        await Assert.ThrowsAsync<ArgumentException>(() => reopened.SetShortcutConfigurationAsync(
            new() { Overrides = new() { ["save"] = "Ctrl+N" } }, commands));
        Assert.Equal("Ctrl+J", (await reopened.GetShortcutConfigurationAsync()).Resolve(commands)["save"]);
        Assert.Null((await reopened.GetShortcutConfigurationAsync()).Resolve(commands)["new"]);
    }
}
