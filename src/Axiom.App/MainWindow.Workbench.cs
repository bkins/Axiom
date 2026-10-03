using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

public partial class MainWindow
{
    private readonly ObservableCollection<WorkspaceExplorerNode> _explorerNodes = [];
    private readonly Dictionary<string, WorkspaceExplorerNode> _explorerDocuments = new(WorkspacePathBoundary.Comparer);
    private readonly SemaphoreSlim _layoutSaveGate = new(1, 1);
    private readonly SemaphoreSlim _explorerReadGate = new(1, 1);
    private readonly Avalonia.Threading.DispatcherTimer _explorerFilterTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private double _toolWidth = 320;
    private bool _workbenchReady;
    private int _explorerGeneration;

    private void InitializeWorkbench()
    {
        FileTree.ItemsSource = _explorerNodes;
        InitializeExplorerDrag();
        _workspaceTree.CollectionChanged += (_, _) => { ++_explorerGeneration; _explorerFilterTimer.Stop(); _explorerFilterTimer.Start(); };
        _explorerFilterTimer.Tick += (_, _) => { _explorerFilterTimer.Stop(); RefreshExplorer(); };
        Closed += (_, _) => { ++_explorerGeneration; _explorerFilterTimer.Stop(); };
        SizeChanged += (_, _) => ClampWorkbenchWidths();
        _workbenchReady = true;
    }

    private async Task RestoreWorkbenchAsync()
    {
        if (_recovery is not IWorkbenchLayoutStore store) return;
        try { ApplyWorkbenchLayout(await store.GetWorkbenchLayoutAsync()); }
        catch (Exception exception) { ReportError("workbench.restore.failed", exception, null); }
    }

    private void ApplyWorkbenchLayout(WorkbenchLayout layout)
    {
        WorkbenchLayout.ColumnDefinitions[0].Width = new GridLength(layout.WorkspaceWidth);
        _toolWidth = layout.ToolWidth;
        SidebarTopics.SelectedIndex = layout.ToolIndex;
        SetToolWindowVisible(layout.ToolVisible);
        ClampWorkbenchWidths();
    }

    private void ClampWorkbenchWidths()
    {
        if (!_workbenchReady) return;
        var available = Math.Max(620, Bounds.Width - (HelpPanelHost.IsVisible ? HelpPanelHost.Width : 0));
        var left = Math.Clamp(WorkbenchLayout.ColumnDefinitions[0].Width.Value, 180, Math.Min(480, available * 0.32));
        WorkbenchLayout.ColumnDefinitions[0].Width = new GridLength(left);
        if (ToolWindowHost.IsVisible)
        {
            var width = Math.Clamp(_toolWidth, 260, Math.Min(480, Math.Max(260, available - left - 290)));
            WorkbenchLayout.ColumnDefinitions[4].Width = new GridLength(width);
        }
    }

    private void SetToolWindowVisible(bool visible)
    {
        if (!visible && ToolWindowHost.IsVisible)
            _toolWidth = Math.Clamp(WorkbenchLayout.ColumnDefinitions[4].ActualWidth, 260, 480);
        ToolWindowHost.IsVisible = ToolWindowSplitter.IsVisible = visible;
        WorkbenchLayout.ColumnDefinitions[3].Width = new GridLength(visible ? 5 : 0);
        WorkbenchLayout.ColumnDefinitions[4].Width = new GridLength(visible ? _toolWidth : 0);
        if (SidebarTopics.SelectedItem is TabItem item) ToolWindowTitle.Text = item.Header?.ToString();
        if (visible) HelpPanelHost.IsVisible = false;
        ClampWorkbenchWidths();
    }

    private async void ToolWindow_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: string tag } || !int.TryParse(tag, out var index)) return;
        ToggleToolWindow(index);
        await PersistWorkbenchAsync();
    }

    private void ToggleToolWindow(int index)
    {
        var wasSame = SidebarTopics.SelectedIndex == index;
        SidebarTopics.SelectedIndex = index;
        SetToolWindowVisible(!ToolWindowHost.IsVisible || !wasSame);
        if (ToolWindowHost.IsVisible && index == 3) WorkspaceSearchBox.Focus();
    }

    private async void CollapseToolWindow_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        SetToolWindowVisible(false);
        Editor.TextArea.Focus();
        await PersistWorkbenchAsync();
    }

    private async void ResetWorkbench_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        ApplyWorkbenchLayout(new());
        HelpPanelHost.IsVisible = false;
        await PersistWorkbenchAsync();
        UpdateChrome("Workbench layout reset; documents and working buffers retained.");
    }

    private async void WorkbenchSplitter_OnPointerReleased(object? sender, PointerReleasedEventArgs eventArgs)
    {
        if (ToolWindowHost.IsVisible) _toolWidth = Math.Clamp(WorkbenchLayout.ColumnDefinitions[4].ActualWidth, 260, 480);
        var left = Math.Clamp(WorkbenchLayout.ColumnDefinitions[0].ActualWidth, 180, 480);
        WorkbenchLayout.ColumnDefinitions[0].Width = new GridLength(left);
        ClampWorkbenchWidths();
        await PersistWorkbenchAsync();
    }

    private async Task PersistWorkbenchAsync()
    {
        if (!_workbenchReady || _recovery is not IWorkbenchLayoutStore store) return;
        await _layoutSaveGate.WaitAsync();
        try
        {
            await store.SetWorkbenchLayoutAsync(new(Math.Clamp(WorkbenchLayout.ColumnDefinitions[0].Width.Value, 180, 480),
                _toolWidth, Math.Clamp(SidebarTopics.SelectedIndex, 0, 5), ToolWindowHost.IsVisible));
        }
        catch (Exception exception) { ReportError("workbench.save.failed", exception, null); }
        finally { _layoutSaveGate.Release(); }
    }

    private void ExplorerFilter_OnTextChanged(object? sender, TextChangedEventArgs eventArgs)
    {
        if (!_workbenchReady) return;
        _explorerFilterTimer.Stop();
        _explorerFilterTimer.Start();
    }

    private async void ExplorerSearch_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        SidebarTopics.SelectedIndex = 3;
        SetToolWindowVisible(true);
        WorkspaceSearchBox.Text = ExplorerFilter.Text ?? "";
        WorkspaceSearchBox.Focus();
        if (!string.IsNullOrWhiteSpace(WorkspaceSearchBox.Text)) await SearchWorkspaceAsync(WorkspaceSearchBox.Text);
        await PersistWorkbenchAsync();
    }

    private void ExplorerFilter_OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Escape)
        {
            ExplorerFilter.Text = "";
            eventArgs.Handled = true;
            FileTree.Focus();
        }
    }

    private void RefreshExplorer()
    {
        if (!_workbenchReady) return;
        _explorerFilterTimer.Stop();
        ++_explorerGeneration;
        var filter = ExplorerFilter.Text?.Trim() ?? "";
        var previous = FileTree.SelectedItem as WorkspaceExplorerNode;
        _explorerDocuments.Clear();
        _explorerNodes.Clear();
        var pinned = new WorkspaceExplorerNode("Pinned", "") { IsGroup = true, IsExpanded = true };
        foreach (var pin in _workspacePins)
            if (filter.Length == 0 || pin.RelativePath.Contains(filter, StringComparison.OrdinalIgnoreCase))
                pinned.Children.Add(new WorkspaceExplorerNode(pin.RelativePath, pin.RelativePath, document: true) { IsPinned = true, IsPinAlias = true });
        if (pinned.Children.Count > 0) _explorerNodes.Add(pinned);
        foreach (var source in _workspaceTree)
            if (BuildExplorerNode(source, filter) is { } node) _explorerNodes.Add(node);
        var count = _explorerDocuments.Count;
        ExplorerFilterStatus.Text = filter.Length == 0 ? "Expand a document for its headings." : $"{count} matching document{(count == 1 ? "" : "s")}; Escape clears filter.";
        RefreshExplorerOutline();
        var restored = previous is null ? null : EnumerateExplorer(_explorerNodes).FirstOrDefault(node =>
            node.Heading is null && node.RelativePath.Equals(previous.RelativePath, WorkspacePathBoundary.Comparison) &&
            node.IsGroup == previous.IsGroup && node.IsPinAlias == previous.IsPinAlias);
        if (restored is not null) FileTree.SelectedItem = restored;
        else RevealActiveExplorerDocument();
    }

    private WorkspaceExplorerNode? BuildExplorerNode(WorkspaceTreeNode source, string filter)
    {
        if (source.IsDocument && filter.Length > 0 && !source.RelativePath.Contains(filter, StringComparison.OrdinalIgnoreCase)) return null;
        var node = new WorkspaceExplorerNode(source.Name, source.RelativePath, source.IsDocument);
        node.IsPinned = source.IsDocument && _workspacePins.Any(pin => pin.RelativePath.Equals(source.RelativePath, WorkspacePathBoundary.Comparison));
        if (source.IsDocument)
        {
            _explorerDocuments[source.RelativePath] = node;
            node.Children.Add(new("Expand to load outline", source.RelativePath, placeholder: true));
        }
        else
        {
            foreach (var child in source.Children)
                if (BuildExplorerNode(child, filter) is { } match) node.Children.Add(match);
            if (filter.Length > 0 && node.Children.Count == 0) return null;
        }
        node.IsExpanded = source.IsExpanded || !node.IsDocument && filter.Length > 0;
        node.Expanded = expandedNode => { source.IsExpanded = expandedNode.IsExpanded; if (expandedNode.IsDocument && expandedNode.IsExpanded) _ = LoadExplorerOutlineAsync(expandedNode); };
        if (node.IsExpanded && node.IsDocument) _ = LoadExplorerOutlineAsync(node);
        return node;
    }

    private static IEnumerable<WorkspaceExplorerNode> EnumerateExplorer(IEnumerable<WorkspaceExplorerNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in EnumerateExplorer(node.Children)) yield return child;
        }
    }

    private void RefreshExplorerOutline()
    {
        if (_folder is null || _session.Path is null || !IsWithinFolder(_session.Path, _folder)) return;
        var relative = Path.GetRelativePath(_folder, _session.Path);
        if (_explorerDocuments.TryGetValue(relative, out var node)) SetExplorerHeadings(node, _outlineEntries, _session.Text);
    }

    private static void SetExplorerHeadings(WorkspaceExplorerNode node, IEnumerable<DocumentOutlineEntry> entries, string source)
    {
        var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source)));
        var expansion = new Dictionary<(int Level, string Text, int Occurrence), bool>();
        var occurrences = new Dictionary<(int Level, string Text), int>();
        foreach (var existing in EnumerateExplorer(node.Children).Where(child => child.Heading is not null))
        {
            var identity = (existing.Heading!.Level, existing.Name);
            occurrences.TryGetValue(identity, out var occurrence);
            expansion[(identity.Level, identity.Name, occurrence)] = existing.IsExpanded;
            occurrences[identity] = occurrence + 1;
        }
        node.Children.Clear();
        occurrences.Clear();
        var parents = new Stack<WorkspaceExplorerNode>();
        foreach (var entry in entries)
        {
            var identity = (entry.Level, entry.Text);
            occurrences.TryGetValue(identity, out var occurrence);
            occurrences[identity] = occurrence + 1;
            var heading = new WorkspaceExplorerNode(entry.Text, node.RelativePath, heading: entry)
            {
                SourceFingerprint = fingerprint
              , IsExpanded = !expansion.TryGetValue((entry.Level, entry.Text, occurrence), out var expanded) || expanded
            };
            while (parents.Count > 0 && parents.Peek().Heading!.Level >= entry.Level) parents.Pop();
            if (parents.Count == 0) node.Children.Add(heading);
            else parents.Peek().Children.Add(heading);
            parents.Push(heading);
        }
    }

    internal async Task LoadExplorerOutlineAsync(WorkspaceExplorerNode node)
    {
        if (!node.IsDocument || _folder is null) return;
        var root = _folder;
        var generation = _explorerGeneration;
        await _explorerReadGate.WaitAsync();
        try
        {
            if (generation != _explorerGeneration || root != _folder || _closingApproved) return;
            var path = Path.Combine(root, node.RelativePath);
            if (FindTab(path) is { } tab) SetExplorerHeadings(node, _outline.Parse(GetTabStructure(tab)), tab.Session.Text);
            else
            {
                var result = await new MarkdownLinkTargetReader(_files).ReadAsync(root, path);
                if (generation != _explorerGeneration || root != _folder || _closingApproved) return;
                if (result.Status != MarkdownLinkResolutionStatus.Resolved || result.Text is null)
                {
                    node.Children.Clear();
                    node.Children.Add(new("Outline unavailable; refresh workspace", node.RelativePath, placeholder: true));
                    return;
                }
                var entries = await Task.Run(() => new DocumentOutlineService().Parse(new MarkdownStructureService().Parse(result.Text)));
                if (generation == _explorerGeneration && root == _folder && !_closingApproved) SetExplorerHeadings(node, entries, result.Text);
            }
        }
        catch (Exception exception) { _log.Error("explorer.outline.failed", exception, node.RelativePath); }
        finally { _explorerReadGate.Release(); }
    }

    private async Task OpenExplorerNodeAsync(WorkspaceExplorerNode node)
    {
        if (_folder is null || node.IsPlaceholder || !node.IsDocument && node.Heading is null) return;
        var root = _folder;
        var path = Path.Combine(root, node.RelativePath);
        await OpenExplorerPathAsync(path);
        if (root != _folder || _session.Path is null || !_session.Path.Equals(path, WorkspacePathBoundary.Comparison)) return;
        if (node.Heading is not { } heading) return;
        // Never trust a stored offset after a file or working buffer changes.
        var current = _outline.Parse(GetCurrentStructure());
        var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(_session.Text)));
        var match = node.SourceFingerprint == fingerprint ? current.FirstOrDefault(entry => entry.Offset == heading.Offset && entry.Text == heading.Text && entry.Level == heading.Level) : null;
        if (match is null)
        {
            var candidates = current.Where(entry => entry.Text == heading.Text && entry.Level == heading.Level).ToArray();
            if (candidates.Length == 1) match = candidates[0];
        }
        if (match is null) { UpdateChrome("This heading changed or became ambiguous. Expand the document again to refresh its outline."); return; }
        SetNavigationPosition(match.Offset);
        _pendingHeadingFragment = GetCurrentStructure().Headings.FirstOrDefault(entry => entry.Source.Offset == match.Offset)?.Fragment;
        await RestoreHeadingFragmentAsync();
        if (!_readingMode) Editor.TextArea.Focus();
    }

    internal IReadOnlyList<WorkspaceExplorerNode> ExplorerNodesForTesting => _explorerNodes;
    internal void FilterExplorerForTesting(string text) { ExplorerFilter.Text = text; RefreshExplorer(); }
    internal Task OpenExplorerNodeForTestingAsync(WorkspaceExplorerNode node) => OpenExplorerNodeAsync(node);
    internal Task SaveWorkbenchForTestingAsync() => PersistWorkbenchAsync();
    internal void ToggleToolWindowForTesting(int index) => ToggleToolWindow(index);
    internal void ApplyWorkbenchForTesting(WorkbenchLayout layout) => ApplyWorkbenchLayout(layout);
}
