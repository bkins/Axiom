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
            var dialog = new ToolbarCustomizationWindow(_editorPreferences.ToolbarCommands, _editorPreferences.CommandPlacement);
            var choice = await dialog.ShowDialog<string?>(this);
            if (choice is null) return new(CommandOutcome.Cancelled, "Toolbar changes cancelled.");
            if (ManagementState is null) return new(CommandOutcome.Unavailable, "Settings storage is unavailable.");
            var value = _editorPreferences with { ToolbarCommands = choice, CommandPlacement = dialog.Placement };
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
        var side = _editorPreferences.CommandPlacement == CommandPlacement.Side;
        var target = side ? (Panel)SideCommandPanel : CommandPanel;
        foreach (var control in _toolbarControls.Values) { CommandPanel.Children.Remove(control); SideCommandPanel.Children.Remove(control); }
        var index = 1;
        foreach (var id in ToolbarLayout.Read(_editorPreferences.ToolbarCommands))
        {
            var control = _toolbarControls[id];
            if (side) target.Children.Add(control); else target.Children.Insert(index++, control);
            foreach (var button in control.GetLogicalDescendants().OfType<Button>().Prepend(control as Button).OfType<Button>())
            {
                button.Width = side ? 34 : double.NaN;
                button.Padding = new Thickness(5); button.MinWidth = button.MinHeight = 30;
            }
        }
        SideCommandHost.IsVisible = side && target.Children.Count > 0 && _viewMode is not WorkbenchViewMode.Zen and not WorkbenchViewMode.Presentation;
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
        foreach (var button in CommandPanel.GetLogicalDescendants().Concat(SideCommandPanel.GetLogicalDescendants()).OfType<Button>())
        {
            button.Padding = new Thickness(compact ? 3 : 5);
            button.MinWidth = button.MinHeight = compact ? 28 : 30;
        }
        DocumentTabs.FontSize = compact ? 12 : 14;
    }

    internal IReadOnlyList<string> VisibleToolbarIdsForTesting => (_editorPreferences.CommandPlacement == CommandPlacement.Side ? SideCommandPanel.Children : CommandPanel.Children).Where(control => control.IsVisible)
        .Select(control => _toolbarControls.FirstOrDefault(pair => ReferenceEquals(pair.Value, control)).Key ?? "menu").ToArray();
}
