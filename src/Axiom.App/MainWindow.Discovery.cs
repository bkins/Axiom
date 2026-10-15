using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Axiom.Core;

namespace Axiom.App;

public partial class MainWindow
{
    private bool _applyingDiscovery;
    private void InitializeDiscovery()
    {
        Grid.SetRowSpan(WorkspaceSidebar, 3); Grid.SetRowSpan(WorkspaceSplitter, 3); Grid.SetRowSpan(ToolWindowRail, 3);
        SidebarTopics.SelectionChanged += (_, _) => ApplyDiscoveryLayout();
        WorkspaceSearchBox.KeyDown += async (_, args) => { if (args.Key == Key.Enter) { args.Handled = true; await ExecuteCommandAsync("workspace.search"); } };
        RegisterCommand("navigation.returnToDiscovery", "Return to retained discovery results", null, false, async (_, _) =>
        {
            SidebarTopics.SelectedIndex = 3; SetToolWindowVisible(true); WorkspaceSearchResults.Focus();
            await PersistWorkbenchAsync();
            return new(CommandOutcome.Completed, "Discovery query and results retained. Searches inspect saved Markdown; open drafts remain unchanged.");
        });
        RegisterCommand("view.discoveryPlacement", "Switch discovery between right panel and bottom results", null, false, async (_, _) =>
        {
            var placement = _editorPreferences.DiscoveryPlacement == DiscoveryPlacement.Right ? DiscoveryPlacement.Bottom : DiscoveryPlacement.Right;
            return await SaveDiscoveryPlacementAsync(placement);
        });
        var returnButton = new Button { Content = "Return to discovery results", Margin = new Avalonia.Thickness(0, 0, 0, 6) };
        returnButton.Click += async (_, _) => await ExecuteCommandAsync("navigation.returnToDiscovery");
        foreach (var index in new[] { 0, 2, 5 })
        {
            var content = ((TabItem)SidebarTopics.Items[index]!).Content as DockPanel;
            if (content is null) continue;
            var button = new Button { Content = returnButton.Content, Margin = returnButton.Margin };
            button.Click += async (_, _) => await ExecuteCommandAsync("navigation.returnToDiscovery");
            DockPanel.SetDock(button, Dock.Top); content.Children.Insert(0, button);
        }
    }
    private void ApplyDiscoveryLayout()
    {
        if (!_workbenchReady || _applyingDiscovery) return;
        _applyingDiscovery = true;
        try
        {
            DiscoveryPlacementChoice.SelectedIndex = (int)_editorPreferences.DiscoveryPlacement;
            var bottom = SidebarTopics.SelectedIndex == 3 && ToolWindowHost.IsVisible && _editorPreferences.DiscoveryPlacement == DiscoveryPlacement.Bottom;
            Grid.SetRow(ToolWindowHost, bottom ? 2 : 0); Grid.SetColumn(ToolWindowHost, bottom ? 2 : 4); Grid.SetColumnSpan(ToolWindowHost, bottom ? 3 : 1);
            Grid.SetRow(ToolWindowSplitter, bottom ? 1 : 0); Grid.SetColumn(ToolWindowSplitter, bottom ? 2 : 3); Grid.SetColumnSpan(ToolWindowSplitter, bottom ? 3 : 1);
            ToolWindowSplitter.ResizeDirection = bottom ? GridResizeDirection.Rows : GridResizeDirection.Columns;
            ToolWindowSplitter.Width = bottom ? double.NaN : 5; ToolWindowSplitter.Height = bottom ? 5 : double.NaN;
            WorkbenchLayout.RowDefinitions[1].Height = new GridLength(bottom ? 5 : 0);
            WorkbenchLayout.RowDefinitions[2].Height = new GridLength(bottom ? Math.Clamp(Bounds.Height * 0.34, 180, 320) : 0);
            WorkbenchLayout.ColumnDefinitions[3].Width = new GridLength(ToolWindowHost.IsVisible && !bottom ? 5 : 0);
            WorkbenchLayout.ColumnDefinitions[4].Width = new GridLength(ToolWindowHost.IsVisible && !bottom ? _toolWidth : 0);
            DiscoveryContext.Text = _cpAnalysisClient is null ? "CP connects automatically when you Analyze or Search. Offline search remains available."
                : "CP connected to " + _cpAnalysisClient.Endpoint.Host + " · Reviewed CP search reviews sources before sending. Offline results stay local; backlinks and explicit relations describe the active document, while CP suggestions are derived.";
            ClampWorkbenchWidths();
        }
        finally { _applyingDiscovery = false; }
    }
    private async Task<CommandResult> SaveDiscoveryPlacementAsync(DiscoveryPlacement placement)
    {
        if (ManagementState is null) return new(CommandOutcome.Unavailable, "Settings storage is unavailable.");
        var value = _editorPreferences with { DiscoveryPlacement = placement };
        await ManagementState.SetEditorPreferencesAsync(value, _appearance); ApplyEditorPreferences(value);
        return new(CommandOutcome.Completed, "Discovery layout saved; query, results, selection, and working buffers retained.");
    }
    private async void DiscoveryPlacement_OnChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (!_workbenchReady || _applyingDiscovery || DiscoveryPlacementChoice.SelectedIndex < 0) return;
        if (DiscoveryPlacementChoice.SelectedIndex == (int)_editorPreferences.DiscoveryPlacement) return;
        try { await SaveDiscoveryPlacementAsync((DiscoveryPlacement)DiscoveryPlacementChoice.SelectedIndex); }
        catch (Exception error) { ReportError("discovery.layout.failed", error, null); ApplyDiscoveryLayout(); }
    }
    private async void DiscoveryOpen_OnClick(object? sender, RoutedEventArgs args) => await ExecuteCommandAsync("navigation.searchResult");
    private async void DiscoveryResults_OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter) return;
        args.Handled = true; await ExecuteCommandAsync("navigation.searchResult");
    }
    private void DiscoveryConnected_OnClick(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: string value } && int.TryParse(value, out var index)) { SidebarTopics.SelectedIndex = index; SetToolWindowVisible(true); }
    }
}
