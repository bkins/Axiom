using Avalonia.Interactivity;

namespace Axiom.App;

public partial class MainWindow
{
    private async void Help_OnClick(object? sender, RoutedEventArgs args) => await ShowHelpAsync(null);
    private async void MetadataHelp_OnClick(object? sender, RoutedEventArgs args) => await ShowHelpAsync("metadata");
    private async void WorkspaceHelp_OnClick(object? sender, RoutedEventArgs args) => await ShowHelpAsync("Workspace loading");
    private async void RelationshipHelp_OnClick(object? sender, RoutedEventArgs args) => await ShowHelpAsync("Explicit relationships");

    private HelpPanel? _persistentHelp;

    private Task ShowHelpAsync(string? context)
    {
        if (HelpRequestedForTesting is { } show) return show(context);
        _persistentHelp ??= new HelpPanel(_operationalPaths.Root, _appearance,
            () => HelpPanelHost.IsVisible = false,
            delta => HelpPanelHost.Width = Math.Clamp(HelpPanelHost.Width + delta, 280, Math.Max(280, Bounds.Width - 550)));
        HelpPanelHost.Child = _persistentHelp;
        HelpPanelHost.IsVisible = true;
        ClampHelpPanelWidth();
        _persistentHelp.SetAppearance(_appearance);
        _persistentHelp.SelectContext(context);
        return Task.CompletedTask;
    }

    private void ClampHelpPanelWidth()
    {
        if (HelpPanelHost.IsVisible) HelpPanelHost.Width = Math.Clamp(HelpPanelHost.Width, 280, Math.Max(280, Bounds.Width - 550));
    }

    internal Func<string?, Task>? HelpRequestedForTesting { get; set; }
    internal Task ShowPersistentHelpForTestingAsync(string? context) => ShowHelpAsync(context);
    internal HelpPanel? PersistentHelpForTesting => _persistentHelp;

    internal string? HelpContext() => RelationshipsTab.IsSelected ? "Explicit relationships" :
        MetadataTab.IsSelected || ClassificationTab.IsSelected ? "Document metadata and classification" :
        FileTree.IsKeyboardFocusWithin ? "Workspace loading" :
        _readingMode ? "Workspace loading, reading mode" : "Follow Markdown links";
}
