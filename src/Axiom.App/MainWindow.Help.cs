using Avalonia.Interactivity;

namespace Axiom.App;

public partial class MainWindow
{
    private async void Help_OnClick(object? sender, RoutedEventArgs args) => await ShowHelpAsync(null);
    private async void MetadataHelp_OnClick(object? sender, RoutedEventArgs args) => await ShowHelpAsync("metadata");
    private async void WorkspaceHelp_OnClick(object? sender, RoutedEventArgs args) => await ShowHelpAsync("Workspace loading");
    private async void RelationshipHelp_OnClick(object? sender, RoutedEventArgs args) => await ShowHelpAsync("Explicit relationships");

    private Task ShowHelpAsync(string? context) => new HelpWindow(context, _operationalPaths.Root, _appearance).ShowDialog(this);

    private string? HelpContext() => MetadataFields.IsKeyboardFocusWithin || ClassificationResults.IsKeyboardFocusWithin ? "metadata" :
        Relationships.IsKeyboardFocusWithin ? "Explicit relationships" : FileTree.IsKeyboardFocusWithin ? "Workspace loading" :
        _readingMode ? "Workspace loading, reading mode" : "Follow Markdown links";
}
