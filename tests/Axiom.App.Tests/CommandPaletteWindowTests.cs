using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Axiom.Core;
using Xunit;

namespace Axiom.App.Tests;

public sealed class CommandPaletteWindowTests
{
    [AvaloniaFact]
    public async Task MatchingShowsEffectiveBindingAndUnavailableReasonWithoutExecuting()
    {
        var dispatcher = new CommandDispatcher();
        var executions = 0;
        Task<CommandResult> Execute(CommandContext context, CancellationToken cancellationToken)
        {
            executions++;
            return Task.FromResult(new CommandResult(CommandOutcome.Completed, "Done"));
        }
        dispatcher.Register(new("save", "Save document", "Ctrl+S", false, _ => new(false, "Document unavailable"), Execute));
        dispatcher.Register(new("new", "New document", "Ctrl+N", false, _ => CommandAvailability.Available, Execute));
        var owner = new Window();
        owner.Show();
        try
        {
            var palette = new CommandPaletteWindow(dispatcher, new("draft", null, null, false, true, false, false, false, true),
                new Dictionary<string, string?> { ["save"] = "Ctrl+J", ["new"] = null });
            var result = palette.ShowDialog<string?>(owner);
            var search = palette.GetLogicalDescendants().OfType<TextBox>().Single();
            var results = palette.GetLogicalDescendants().OfType<ListBox>().Single();
            var run = palette.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "Run"));
            search.Text = "SAVE";
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var disabled = Assert.Single(results.ItemsSource!.Cast<CommandPaletteEntry>());
            Assert.Contains("Ctrl+J", disabled.Display);
            Assert.DoesNotContain("Ctrl+S", disabled.Display);
            Assert.Contains("Document unavailable", disabled.Display);
            Assert.False(run.IsEnabled);
            run.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(result.IsCompleted);
            search.Text = "unknown";
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, results.ItemCount);
            Assert.False(run.IsEnabled);
            search.Text = "new";
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(run.IsEnabled);
            Assert.DoesNotContain("Ctrl+N", Assert.Single(results.ItemsSource!.Cast<CommandPaletteEntry>()).Display);
            run.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("new", await result);
            Assert.Equal(0, executions);
        }
        finally { owner.Close(); }
    }
}
