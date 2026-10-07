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
    public void NewWorkbenchDefaultPreservesExistingExplicitBindingWithoutMutatingConfiguration()
    {
        var commands = new[] { Command("save", "Ctrl+S"), Command("new", "Ctrl+N"), Command("view.search", "Ctrl+Shift+F") };
        var configuration = new ShortcutConfiguration { Overrides = new() { ["save"] = "shift+ctrl+f", ["new"] = "Ctrl+J" } };
        var resolved = configuration.Resolve(commands);
        Assert.Equal("Ctrl+Shift+F", resolved["save"]);
        Assert.Equal("Ctrl+J", resolved["new"]);
        Assert.Null(resolved["view.search"]);
        Assert.Equal(2, configuration.Overrides.Count);
        Assert.Equal("shift+ctrl+f", configuration.Overrides["save"]);
        Assert.Equal("Ctrl+Shift+F", new ShortcutConfiguration().Resolve(commands)["view.search"]);
        Assert.Throws<ArgumentException>(() => (configuration with
        {
            Overrides = new(configuration.Overrides) { ["view.search"] = "Ctrl+Shift+F" }
        }).Resolve(commands));
    }

    [Fact]
    public void ConflictsNativeUndoOverridesAndFutureFormatsAreRejected()
    {
        var commands = new[] { Command("save", "Ctrl+S"), Command("editor.undo", "Ctrl+Z") };
        Assert.Throws<ArgumentException>(() => new ShortcutConfiguration { Overrides = new() { ["save"] = "ctrl+z" } }.Resolve(commands));
        Assert.Throws<ArgumentException>(() => new ShortcutConfiguration { Overrides = new() { ["editor.undo"] = null } }.Resolve(commands));
        Assert.Throws<ArgumentException>(() => new ShortcutConfiguration { FormatVersion = 2 }.Resolve(commands));
    }

    [Fact]
    public void NewSaveAllDefaultPreservesExistingExplicitShortcuts()
    {
        var commands = new[] { Command("document.save", null!), Command("document.saveAll", "Ctrl+S") };
        Assert.Equal("Ctrl+S", new ShortcutConfiguration().Resolve(commands)["document.saveAll"]);
        var configuration = new ShortcutConfiguration { Overrides = new() { ["document.save"] = "Ctrl+S" } };
        var resolved = configuration.Resolve(commands);
        Assert.Equal("Ctrl+S", resolved["document.save"]);
        Assert.Null(resolved["document.saveAll"]);
    }
}
