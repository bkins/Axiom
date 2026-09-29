using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

public partial class MainWindow
{
    private readonly ObservableCollection<WorkspaceLinkReference> _backlinks = [];
    private readonly DispatcherTimer _backlinkRefreshTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private CancellationTokenSource? _backlinkCancellation;
    private int _backlinkGeneration;
    private WorkspaceBacklinkIndex? _savedLinkIndex;
    private string? _linkIndexRoot;
    private string _linkIndexState = "Open a workspace to index Markdown references.";

    private void InitializeBacklinks()
    {
        Backlinks.ItemsSource = _backlinks;
        _backlinkRefreshTimer.Tick += async (_, _) => { _backlinkRefreshTimer.Stop(); await RebuildBacklinksAsync(); };
        Closed += (_, _) => { ++_backlinkGeneration; _backlinkCancellation?.Cancel(); _backlinkRefreshTimer.Stop(); };
    }

    private void ScheduleBacklinkRebuild()
    {
        if (_closingApproved || _folder is null) return;
        ++_backlinkGeneration;
        _backlinkCancellation?.Cancel();
        _linkIndexState = "Stale — saved Markdown changed; refresh pending.";
        RefreshIncomingReferences();
        _backlinkRefreshTimer.Stop();
        _backlinkRefreshTimer.Start();
    }

    private void BeginBacklinkWorkspace(string root)
    {
        if (_linkIndexRoot == root) return;
        ++_backlinkGeneration;
        _backlinkCancellation?.Cancel();
        _savedLinkIndex = null;
        _linkIndexRoot = root;
        _backlinks.Clear();
        _linkIndexState = "Stale — this workspace has not been indexed yet.";
    }

    private async void RebuildBacklinks_OnClick(object? sender, RoutedEventArgs eventArgs) => await RebuildBacklinksAsync();
    private void CancelBacklinks_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        _backlinkCancellation?.Cancel();
        _backlinkRefreshTimer.Stop();
        _linkIndexState = "Stale — indexing cancelled; saved results may be incomplete.";
        RefreshIncomingReferences();
    }

    private async Task RebuildBacklinksAsync()
    {
        if (_activeWorkspace is not { } workspace || _closingApproved) return;
        _backlinkRefreshTimer.Stop();
        _backlinkCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _backlinkCancellation = cancellation;
        var generation = ++_backlinkGeneration;
        var root = workspace.RootPath;
        if (_linkIndexRoot != root) { _savedLinkIndex = null; _linkIndexRoot = root; }
        _linkIndexState = "Indexing saved Markdown…";
        RefreshIncomingReferences();
        try
        {
            if (_savedLinkIndex is null && _recovery is IWorkspaceLinkIndexStore cached)
            {
                try
                {
                    var cachedIndex = await cached.GetLinkIndexAsync(workspace.WorkspaceId, cancellation.Token);
                    if (generation != _backlinkGeneration || _folder != root) return;
                    _savedLinkIndex = cachedIndex;
                }
                catch (Exception exception) when (exception is InvalidDataException or JsonException)
                {
                    _log.Error("backlinks.cache.invalid", exception, root);
                    _linkIndexState = "Rebuilding an invalid saved link cache from Markdown…";
                }
            }
            var progress = new Progress<WorkspaceIndexProgress>(value =>
            {
                if (generation != _backlinkGeneration || _folder != root) return;
                _linkIndexState = $"Indexing {value.ProcessedFiles}/{value.TotalFiles} saved files…";
                BacklinkStatus.Text = _linkIndexState;
            });
            var service = new WorkspaceLinkIndexService(_files, new MarkdownLinkTargetReader(_files));
            var previous = _savedLinkIndex;
            var index = await Task.Run(() => service.BuildAsync(root, progress, cancellation.Token, previous), cancellation.Token);
            if (generation != _backlinkGeneration || _folder != root || _closingApproved) return;
            if (_recovery is IWorkspaceLinkIndexStore store) await store.SaveLinkIndexAsync(workspace.WorkspaceId, index, cancellation.Token);
            if (generation != _backlinkGeneration || _folder != root || cancellation.IsCancellationRequested) return;
            _savedLinkIndex = index;
            _linkIndexState = index.Errors.Count == 0 ? $"Complete — {index.Documents.Count} saved files."
                : $"Partial — {index.Documents.Count} saved files; {index.Errors.Count} skipped/error paths. " + string.Join("; ", index.Errors.Take(3));
            RefreshIncomingReferences();
        }
        catch (OperationCanceledException)
        {
            if (generation == _backlinkGeneration) { _linkIndexState = "Stale — indexing cancelled; previous results retained."; RefreshIncomingReferences(); }
        }
        catch (Exception exception)
        {
            if (generation != _backlinkGeneration) return;
            _linkIndexState = "Partial/Error — " + exception.Message;
            _log.Error("backlinks.index.failed", exception, root);
            RefreshIncomingReferences();
        }
        finally { cancellation.Dispose(); if (_backlinkCancellation == cancellation) _backlinkCancellation = null; }
    }

    private void RefreshIncomingReferences()
    {
        _backlinks.Clear();
        if (_folder is not { } root || _session.Path is not { } active || !IsWithinFolder(active, root))
        {
            BacklinkStatus.Text = "Save a document inside the workspace to inspect incoming references.";
            return;
        }
        var overlays = new Dictionary<string, IReadOnlyList<WorkspaceLinkReference>>(StringComparer.OrdinalIgnoreCase);
        foreach (var tab in _tabs.Where(tab => tab.Session.IsDirty && tab.Session.Path is not null && IsWithinFolder(tab.Session.Path, root)))
        {
            var structure = GetTabStructure(tab);
            var references = structure.Links.Select(link => WorkspaceLinkIndexService.CreateReference(root, tab.Session.Path!, structure, link,
                OverlayResolution(root, tab.Session.Path!, link.Destination), true)).ToArray();
            overlays[Path.GetRelativePath(root, tab.Session.Path!)] = references;
        }
        foreach (var reference in WorkspaceLinkIndexService.Incoming(Path.GetRelativePath(root, active), _savedLinkIndex, overlays)) _backlinks.Add(reference);
        BacklinkStatus.Text = $"{_linkIndexState} {_backlinks.Count} incoming references; Unsaved results reflect open buffers.";
    }

    private MarkdownLinkResolution OverlayResolution(string root, string source, string destination)
    {
        var result = MarkdownLinkResolver.ResolveLocation(root, source, destination);
        if (result.Status != MarkdownLinkResolutionStatus.Resolved) return result;
        if (FindTab(result.TargetPath!) is { } open)
        {
            if (result.Fragment is { Length: > 0 } fragment && GetTabStructure(open).FindHeading(fragment) is null)
                return result with { Status = MarkdownLinkResolutionStatus.MissingHeading };
            return result;
        }
        return result with { Status = MarkdownLinkResolutionStatus.Unverified };
    }

    private async void Backlinks_OnDoubleTapped(object? sender, RoutedEventArgs eventArgs)
    {
        if (Backlinks.SelectedItem is WorkspaceLinkReference reference) await OpenBacklinkAsync(reference);
    }

    private async void Backlinks_OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key != Key.Enter || eventArgs.KeyModifiers != KeyModifiers.None
            || Backlinks.SelectedItem is not WorkspaceLinkReference reference) return;
        eventArgs.Handled = true;
        await OpenBacklinkAsync(reference);
    }

    private async Task OpenBacklinkAsync(WorkspaceLinkReference reference)
    {
        if (_sessionTransitionRunning || _navigationRunning || _folder is not { } root) return;
        if (!await _linkNavigationGate.WaitAsync(0)) return;
        _navigationRunning = true;
        SetSessionCommandsEnabled(false);
        try
        {
            var path = WorkspaceMutationPolicy.Resolve(root, reference.SourceRelativePath);
            var source = await new MarkdownLinkTargetReader(_files).ReadAsync(root, path);
            if (source.Status != MarkdownLinkResolutionStatus.Resolved) { UpdateChrome("The backlink source is unavailable; rebuild the index."); return; }
            var structure = FindTab(path) is { } open ? GetTabStructure(open) : _structureParser.Parse(source.Text ?? string.Empty);
            MarkdownDocumentLink? link;
            var indexed = _savedLinkIndex?.Documents.FirstOrDefault(document => document.RelativePath.Equals(reference.SourceRelativePath, StringComparison.OrdinalIgnoreCase));
            if (reference.IsUnsaved || indexed?.ContentHash == WorkspaceLinkIndexService.Hash(structure.SourceText))
                link = structure.Links.FirstOrDefault(candidate => candidate.Source.Offset == reference.Source.Offset && candidate.Destination == reference.Destination);
            else
            {
                var candidates = structure.Links.Where(candidate => candidate.Destination == reference.Destination).ToArray();
                link = candidates.Length == 1 ? candidates[0] : null;
            }
            if (link is null) { UpdateChrome("The backlink changed or is ambiguous; rebuild the index before navigating."); return; }
            var previous = CurrentNavigationLocation();
            await OpenDocumentAsync(path);
            if (!string.Equals(_session.Path, path, StringComparison.OrdinalIgnoreCase)) return;
            SetNavigationPosition(link.Source.Offset);
            if (previous is not null) _backLocations.Add(previous);
            _forwardLocations.Clear();
            UpdateNavigationCommands();
            UpdateChrome("Opened the Markdown reference at its source location.");
        }
        catch (Exception exception) { ReportError("backlinks.navigate.failed", exception, root); }
        finally
        {
            _navigationRunning = false;
            SetSessionCommandsEnabled(true);
            UpdateNavigationCommands();
            _linkNavigationGate.Release();
        }
    }

    internal Task RebuildBacklinksForTestingAsync() => RebuildBacklinksAsync();
    internal IReadOnlyList<WorkspaceLinkReference> BacklinksForTesting => _backlinks;
    internal Task OpenBacklinkForTestingAsync(WorkspaceLinkReference reference) => OpenBacklinkAsync(reference);
}
