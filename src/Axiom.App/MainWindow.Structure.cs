using Axiom.Core;

namespace Axiom.App;

public partial class MainWindow
{
    private async void DocumentStructure_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs) => await ExecuteCommandAsync("document.structure");

    private async Task<CommandResult> ShowDocumentStructureAsync()
    {
        var tab = _activeTab;
        var selected = await new StructureBrowserWindow(GetCurrentStructure()).ShowDialog<DocumentStructureNode?>(this);
        if (selected is null) return new(CommandOutcome.Completed, "Structure browser closed.");
        if (_activeTab != tab) return new(CommandOutcome.Cancelled, "Document changed while browsing structure.");
        var current = GetCurrentStructure().Nodes.FirstOrDefault(node => node.NodeId == selected.NodeId);
        if (current is null) return new(CommandOutcome.Unavailable, "This structure node changed or became ambiguous. Refresh the structure browser.");
        SetNavigationPosition(current.Source.Offset);
        Editor.Select(current.Source.Offset, current.Source.Length);
        Editor.Focus();
        return new(CommandOutcome.Completed, "Selected " + current.Kind + " source at line " + current.Source.LineNumber);
    }
}
