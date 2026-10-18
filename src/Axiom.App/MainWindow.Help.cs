using Avalonia.Interactivity;

namespace Axiom.App;

public partial class MainWindow
{
    private async void Help_OnClick(object? sender, RoutedEventArgs args) => await ExecuteCommandAsync("help.home");
    private async void MetadataHelp_OnClick(object? sender, RoutedEventArgs args) => await ExecuteCommandAsync("help.metadata");
    private async void WorkspaceHelp_OnClick(object? sender, RoutedEventArgs args) => await ExecuteCommandAsync("help.workspace");
    private async void RelationshipHelp_OnClick(object? sender, RoutedEventArgs args) => await ExecuteCommandAsync("help.relationships");

    private HelpPanel? _persistentHelp;

    private Task ShowHelpAsync(string? context)
    {
        if (HelpRequestedForTesting is { } show) return show(context);
        _persistentHelp ??= new HelpPanel(_operationalPaths.Root, _appearance,
            () => { HelpPanelHost.IsVisible = false; SetToolWindowVisible(false); },
            delta => { WorkbenchLayout.ColumnDefinitions[4].Width = new Avalonia.Controls.GridLength(Math.Clamp(WorkbenchLayout.ColumnDefinitions[4].Width.Value + delta, 280, Math.Max(280, Bounds.Width - 550))); ClampWorkbenchWidths(); },
            () => _ = ExecuteCommandAsync("help.walkthrough"), _commands.Commands);
        HelpPanelHost.Child = _persistentHelp;
        SetToolWindowVisible(false);
        HelpPanelHost.IsVisible = true;
        WorkbenchLayout.ColumnDefinitions[4].Width = new Avalonia.Controls.GridLength(340);
        ClampHelpPanelWidth();
        ClampWorkbenchWidths();
        _persistentHelp.SetAppearance(_appearance);
        _persistentHelp.SelectContext(context);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (HelpPanelHost.IsVisible) _persistentHelp.FocusSearch();
        }, Avalonia.Threading.DispatcherPriority.Loaded);
        return Task.CompletedTask;
    }

    private void ClampHelpPanelWidth()
    {
        if (HelpPanelHost.IsVisible) WorkbenchLayout.ColumnDefinitions[4].Width = new Avalonia.Controls.GridLength(Math.Clamp(WorkbenchLayout.ColumnDefinitions[4].Width.Value, 280, Math.Max(280, Bounds.Width - 550)));
    }

    internal Func<string?, Task>? HelpRequestedForTesting { get; set; }
    internal Task ShowPersistentHelpForTestingAsync(string? context) => ShowHelpAsync(context);
    internal HelpPanel? PersistentHelpForTesting => _persistentHelp;

    internal string? HelpContext() => ToolWindowHost.IsVisible && SidebarTopics.SelectedIndex == 0 ? "Explicit relationships" :
        ToolWindowHost.IsVisible && SidebarTopics.SelectedIndex is 1 or 2 ? "Document metadata and classification" :
        ToolWindowHost.IsVisible && SidebarTopics.SelectedIndex is 3 or 6 ? "Discovery and embedded Git terminal" :
        ToolWindowHost.IsVisible && SidebarTopics.SelectedIndex == 4 ? "Navigate by document outline" :
        ToolWindowHost.IsVisible && SidebarTopics.SelectedIndex == 5 ? "Incoming Markdown references" :
        FileTree.IsKeyboardFocusWithin ? "Workspace loading" :
        _readingMode ? "Workspace loading, reading mode" : "Follow Markdown links";
}
