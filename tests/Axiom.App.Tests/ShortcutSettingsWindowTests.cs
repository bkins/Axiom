using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Axiom.Core;
using Xunit;

namespace Axiom.App.Tests;

public sealed class ShortcutSettingsWindowTests
{
    private static IReadOnlyList<AxiomCommand> Commands => new[]
    {
        new AxiomCommand("save", "Save", "Ctrl+S", false, _ => CommandAvailability.Available,
            (_, _) => Task.FromResult(new CommandResult(CommandOutcome.Completed, "Done")))
      , new AxiomCommand("new", "New", "Ctrl+N", false, _ => CommandAvailability.Available,
            (_, _) => Task.FromResult(new CommandResult(CommandOutcome.Completed, "Done")))
    };

    private static void Click(Window window, string title) => window.GetLogicalDescendants().OfType<Button>()
        .Single(button => Equals(button.Content, title)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public async Task ConflictStaysOpenAndResetRequiresApplyWhileCancelDiscardsStaging()
    {
        var current = new ShortcutConfiguration { Overrides = new() { ["save"] = "Ctrl+J" } };
        var owner = new Window();
        owner.Show();
        try
        {
            var dialog = new ShortcutSettingsWindow(Commands, current);
            var result = dialog.ShowDialog<ShortcutConfiguration?>(owner);
            dialog.GetLogicalDescendants().OfType<TextBox>().First().Text = "Ctrl+N";
            Click(dialog, "Apply");
            Assert.False(result.IsCompleted);
            Assert.Contains(dialog.GetLogicalDescendants().OfType<TextBlock>(), block => block.Text?.Contains("conflicts") == true);
            Click(dialog, "Reset defaults");
            Assert.False(result.IsCompleted);
            Assert.Equal("Ctrl+J", current.Overrides["save"]);
            Click(dialog, "Cancel");
            Assert.Null(await result);
            Assert.Equal("Ctrl+J", current.Overrides["save"]);
            var reset = new ShortcutSettingsWindow(Commands, current);
            var applied = reset.ShowDialog<ShortcutConfiguration?>(owner);
            Click(reset, "Reset defaults");
            Click(reset, "Apply");
            Assert.Empty((await applied)!.Overrides);
        }
        finally { owner.Close(); }
    }
}
