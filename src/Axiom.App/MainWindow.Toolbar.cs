using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Axiom.Core;

namespace Axiom.App;

public partial class MainWindow
{
    private readonly Dictionary<string, Control> _toolbarControls = new(StringComparer.Ordinal);

    private void InitializeToolbarPersonalization()
    {
        foreach (var definition in ToolbarLayout.Commands)
        {
            var button = CommandPanel.GetLogicalDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == definition.AutomationName);
            _toolbarControls[definition.Id] = CommandPanel.Children.Single(control => ReferenceEquals(control, button) || control.GetLogicalDescendants().Contains(button));
        }
        RegisterCommand("application.toolbar", "Customize toolbar visibility and order", null, false, async (_, _) =>
        {
            var choice = await new ToolbarCustomizationWindow(_editorPreferences.ToolbarCommands).ShowDialog<string?>(this);
            if (choice is null) return new(CommandOutcome.Cancelled, "Toolbar changes cancelled.");
            if (ManagementState is null) return new(CommandOutcome.Unavailable, "Settings storage is unavailable.");
            var value = _editorPreferences with { ToolbarCommands = choice };
            await ManagementState.SetEditorPreferencesAsync(value, _appearance);
            ApplyEditorPreferences(value);
            return new(CommandOutcome.Completed, "Toolbar configuration saved. Hidden commands remain available from the palette and shortcuts.");
        });
        var tools = ((MenuFlyout)FileCommandsButton.Flyout!).Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Tools"));
        var toolbar = new MenuItem { Header = "Customize Toolbar…" };
        toolbar.Click += async (_, _) => await ExecuteCommandAsync("application.toolbar"); tools.Items.Add(toolbar);
        var settings = new MenuItem { Header = "Settings…" };
        settings.Click += async (_, _) => await ExecuteCommandAsync("application.settings"); tools.Items.Add(settings);
    }

    private void ApplyToolbarLayout()
    {
        if (_toolbarControls.Count == 0) return;
        var index = 1;
        foreach (var id in ToolbarLayout.Read(_editorPreferences.ToolbarCommands))
        {
            var control = _toolbarControls[id]; CommandPanel.Children.Remove(control); CommandPanel.Children.Insert(index++, control);
        }
        ApplyToolbarVisibility();
        ApplyWorkbenchDensity();
    }

    private void ApplyToolbarVisibility()
    {
        var ids = ToolbarLayout.Read(_editorPreferences.ToolbarCommands).ToHashSet(StringComparer.Ordinal);
        foreach (var (id, control) in _toolbarControls)
        {
            var contextVisible = id != "document.enroll" || _session.DocumentId is null;
            if (id == "document.pin") contextVisible = _activeWorkspace is not null && _session.Path is not null && IsWithinFolder(_session.Path, _activeWorkspace.RootPath) && IsMarkdownPath(_session.Path);
            control.IsVisible = contextVisible && ids.Contains(id);
        }
    }

    private void ApplyWorkbenchDensity()
    {
        var compact = _editorPreferences.CompactWorkbench;
        TopCommandBar.Padding = compact ? new Thickness(4,1) : new Thickness(6,3);
        foreach (var button in CommandPanel.GetLogicalDescendants().OfType<Button>())
        {
            button.Padding = new Thickness(compact ? 3 : 5);
            button.MinWidth = button.MinHeight = compact ? 28 : 30;
        }
        DocumentTabs.FontSize = compact ? 12 : 14;
    }

    internal IReadOnlyList<string> VisibleToolbarIdsForTesting => CommandPanel.Children.Where(control => control.IsVisible)
        .Select(control => _toolbarControls.FirstOrDefault(pair => ReferenceEquals(pair.Value, control)).Key ?? "menu").ToArray();
}
