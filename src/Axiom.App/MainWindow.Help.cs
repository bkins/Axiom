using Avalonia.Interactivity;

namespace Axiom.App;

public partial class MainWindow
{
    private async void Help_OnClick(object? sender, RoutedEventArgs args) => await ShowHelpAsync(null);
    private async void MetadataHelp_OnClick(object? sender, RoutedEventArgs args) => await ShowHelpAsync("metadata");
    private async void WorkspaceHelp_OnClick(object? sender, RoutedEventArgs args) => await ShowHelpAsync("Workspace loading");
    private async void RelationshipHelp_OnClick(object? sender, RoutedEventArgs args) => await ShowHelpAsync("Explicit relationships");

    private Task ShowHelpAsync(string? context) => HelpRequestedForTesting is { } show
        ? show(context) : new HelpWindow(context, _operationalPaths.Root, _appearance).ShowDialog(this);

    internal Func<string?, Task>? HelpRequestedForTesting { get; set; }

    internal string? HelpContext() => RelationshipsTab.IsSelected ? "Explicit relationships" :
        MetadataTab.IsSelected || ClassificationTab.IsSelected ? "Document metadata and classification" :
        FileTree.IsKeyboardFocusWithin ? "Workspace loading" :
        _readingMode ? "Workspace loading, reading mode" : "Follow Markdown links";
}
