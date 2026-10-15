using Avalonia.Controls;
using Avalonia.VisualTree;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

public partial class MainWindow
{
    private WorkspacePin? SelectedWorkspacePin => FileTree.SelectedItem is WorkspaceExplorerNode { IsDocument: true } node
        ? _workspacePins.FirstOrDefault(pin => pin.RelativePath.Equals(node.RelativePath, WorkspacePathBoundary.Comparison)) : null;
    private string? SelectedCreationFolder(WorkspaceExplorerNode? requested = null)
    {
        if (_folder is null) return null;
        var selected = requested ?? FileTree.SelectedItem as WorkspaceExplorerNode;
        var relative = selected is { IsGroup: false, IsPlaceholder: false }
            ? selected.IsDocument || selected.Heading is not null ? Path.GetDirectoryName(selected.RelativePath) ?? "" : selected.RelativePath
            : "";
        try
        {
            var path = relative.Length == 0 ? _folder : WorkspaceMutationPolicy.Resolve(_folder, relative, false);
            WorkspaceFileOperations.ValidateAncestors(_folder, path);
            return Directory.Exists(path) ? path : _folder;
        }
        catch (ArgumentException) { return _folder; }
        catch (IOException) { return _folder; }
    }

    private async Task NewDocumentInSelectedFolderAsync(WorkspaceExplorerNode? requested)
    {
        var folder = SelectedCreationFolder(requested);
        _initialPlaceholder = false;
        await LoadSessionAsync(_documents.NewAxiomDocument(), folder is null ? "New unsaved Axiom document" : $"New unsaved Axiom document — Save defaults to {folder}");
        _activeTab.SuggestedSaveFolder = folder;
        ExitViewMode();
        ApplyReadingMode(false);
        Editor.CaretOffset = Editor.Document.TextLength;
        Editor.TextArea.Focus();
    }

    private void RevealActiveExplorerDocument()
    {
        if (_folder is null || _session.Path is null || !IsWithinFolder(_session.Path, _folder)) return;
        var relative = Path.GetRelativePath(_folder, _session.Path);
        if (!_explorerDocuments.TryGetValue(relative, out var node))
        {
            if (!string.IsNullOrWhiteSpace(ExplorerFilter.Text))
                ExplorerFilterStatus.Text = $"Active document hidden by filter: {relative}. Escape clears filter.";
            return;
        }
        ExpandExplorerAncestors(_explorerNodes, node);
        FileTree.SelectedItem = node;
        // Selecting/expanding the tree must retain the user's current input focus.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (FileTree.SelectedItem == node && FileTree.GetVisualDescendants().OfType<TreeViewItem>().FirstOrDefault(item => ReferenceEquals(item.DataContext, node)) is { } container)
                container.BringIntoView();
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private static bool ExpandExplorerAncestors(IEnumerable<WorkspaceExplorerNode> nodes, WorkspaceExplorerNode target)
    {
        foreach (var node in nodes)
        {
            if (ReferenceEquals(node, target)) return true;
            if (!ExpandExplorerAncestors(node.Children, target)) continue;
            node.IsExpanded = true;
            return true;
        }
        return false;
    }

    internal string? SelectedCreationFolderForTesting => SelectedCreationFolder();
}
