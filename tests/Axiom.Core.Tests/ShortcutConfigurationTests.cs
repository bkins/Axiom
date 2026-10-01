using Axiom.Core;

namespace Axiom.Core.Tests;

public sealed class ShortcutConfigurationTests
{
    private static AxiomCommand Command(string id, string? shortcut) => new(id, id, shortcut, false,
        _ => CommandAvailability.Available, (_, _) => Task.FromResult(new CommandResult(CommandOutcome.Completed, "Done")));

    [Fact]
    public void OverridesNormalizeDisableAndIgnoreRetiredCommands()
    {
        var configuration = new ShortcutConfiguration
        {
            Overrides = new() { ["save"] = " shift + ctrl + j", ["new"] = null, ["retired"] = "invalid" }
        };
        var resolved = configuration.Resolve(new[] { Command("save", "Ctrl+S"), Command("new", "Ctrl+N") });
        Assert.Equal("Ctrl+Shift+J", resolved["save"]);
        Assert.Null(resolved["new"]);
        Assert.False(resolved.ContainsKey("retired"));
    }

    [Theory]
    [InlineData("Ctrl+C")]
    [InlineData("Ctrl+Shift+P")]
    [InlineData("Ctrl+Alt+J")]
    [InlineData("J")]
    [InlineData("Shift+J")]
    [InlineData("Ctrl+Ctrl+J")]
    [InlineData("F13")]
    public void ReservedAndInvalidGesturesAreRejected(string gesture)
    {
        var configuration = new ShortcutConfiguration { Overrides = new() { ["save"] = gesture } };
        Assert.Throws<ArgumentException>(() => configuration.Resolve(new[] { Command("save", "Ctrl+S") }));
    }

    [Fact]
    public void ConflictsNativeUndoOverridesAndFutureFormatsAreRejected()
    {
        var commands = new[] { Command("save", "Ctrl+S"), Command("editor.undo", "Ctrl+Z") };
        Assert.Throws<ArgumentException>(() => new ShortcutConfiguration { Overrides = new() { ["save"] = "ctrl+z" } }.Resolve(commands));
        Assert.Throws<ArgumentException>(() => new ShortcutConfiguration { Overrides = new() { ["editor.undo"] = null } }.Resolve(commands));
        Assert.Throws<ArgumentException>(() => new ShortcutConfiguration { FormatVersion = 2 }.Resolve(commands));
    }
}
