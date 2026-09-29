using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

public partial class MainWindow
{
    private bool _explorerOpening;
    private string? _pendingExplorerPath;

    private async void FileTree_OnPointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (args.InitialPressMouseButton != MouseButton.Left || args.Source is not Control control || _folder is null) return;
        var item = control as TreeViewItem ?? control.GetVisualAncestors().OfType<TreeViewItem>().FirstOrDefault();
        if (item?.DataContext is not WorkspaceTreeNode { IsDocument: true } node) return;
        await OpenExplorerPathAsync(Path.Combine(_folder, node.RelativePath));
    }

    private async void FileTree_OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter || args.KeyModifiers != KeyModifiers.None || _folder is null || FileTree.SelectedItem is not WorkspaceTreeNode { IsDocument: true } node) return;
        args.Handled = true;
        await OpenExplorerPathAsync(Path.Combine(_folder, node.RelativePath));
    }

    internal async Task OpenExplorerPathAsync(string path)
    {
        _pendingExplorerPath = path;
        if (_explorerOpening) return;
        if (_sessionTransitionRunning || _navigationRunning || !await ResolvePendingMetadataAsync()) return;
        if (!await _linkNavigationGate.WaitAsync(0)) return;
        _explorerOpening = true;
        _navigationRunning = true;
        SetSessionCommandsEnabled(false);
        try
        {
            while (_pendingExplorerPath is { } requested)
            {
                _pendingExplorerPath = null;
                if (_folder is null || _sessionTransitionRunning || !IsWithinFolder(requested, _folder)) continue;
                var read = await new MarkdownLinkTargetReader(_files).ReadAsync(_folder, requested);
                if (read.Status != MarkdownLinkResolutionStatus.Resolved) { UpdateChrome("This explorer document is missing or unavailable. Refresh the workspace."); continue; }
                await OpenDocumentAsync(requested);
            }
        }
        finally
        {
            _explorerOpening = false;
            _navigationRunning = false;
            SetSessionCommandsEnabled(true);
            _linkNavigationGate.Release();
        }
    }
}
