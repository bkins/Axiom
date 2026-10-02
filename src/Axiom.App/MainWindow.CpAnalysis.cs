using Axiom.Core;
using Axiom.Infrastructure;
using Avalonia.Platform.Storage;

namespace Axiom.App;

public partial class MainWindow
{
    private CpDocumentAnalysisClient? _cpAnalysisClient;

    private async void CpWorkspaceAnalysis_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs)
        => await ExecuteCommandAsync("workspace.cpAnalysis");

    private async Task<CommandResult> ShowCpWorkspaceAnalysisAsync()
    {
        if (_cpAnalysisClient is null) return new(CommandOutcome.Unavailable, "Connect through CP connection and reviewed insights first.");
        var sourceTabs = _tabs.Where(tab => tab == _activeTab || tab.Session.Path is { } path && _activeWorkspace is not null && WorkspacePathBoundary.Contains(_activeWorkspace.RootPath, path)).Take(16).ToArray();
        var entries = sourceTabs.Select((tab, index) =>
        {
            var label = new string(Path.GetFileName(tab.Session.Path ?? "unsaved").Select(character => character is '|' or '[' or ']' || char.IsControl(character) ? '_' : character).ToArray());
            if (label.Length > 90) label = label[..90];
            return (Tab: tab, Snapshot: AnalysisSnapshot.Capture("buffer-" + (index + 1) + "-" + label, tab == _activeTab ? Editor.Text ?? "" : tab.Session.Text));
        }).ToArray();
        bool Fresh(IReadOnlyList<AnalysisSnapshot> selected) => selected.All(source => entries.Any(entry => entry.Snapshot.ReferenceKey == source.ReferenceKey
            && _tabs.Contains(entry.Tab) && entry.Tab.Session.Conflict is null
            && source.Matches(entry.Tab == _activeTab ? Editor.Text ?? "" : entry.Tab.Session.Text)));
        var classifications = entries.ToDictionary(entry => entry.Snapshot.ReferenceKey, entry => entry.Tab.Session.DocumentId is { } id ? _classificationCatalog?.Find(id)?.Classification : null, StringComparer.Ordinal);
        var savedPaths = new Dictionary<string, string>(StringComparer.Ordinal);
        var savedSnapshots = new Dictionary<string, AnalysisSnapshot>(StringComparer.Ordinal);
        var workspaceRoot = _activeWorkspace?.RootPath;
        var reader = new WorkspaceAnalysisSnapshotReader(_files);
        async Task<IReadOnlyList<AnalysisSnapshot>> LoadSavedAsync()
        {
            if (workspaceRoot is null) throw new InvalidOperationException("Activate a workspace first.");
            var chosen = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select saved workspace Markdown for local review"
              , AllowMultiple = true
              , FileTypeFilter = [new FilePickerFileType("Markdown") { Patterns = ["*.md", "*.markdown"] }]
            });
            if (chosen.Count > 16) throw new InvalidOperationException("Add at most 16 saved documents per selection.");
            if (savedPaths.Count + chosen.Count > 64) throw new InvalidOperationException("Review at most 64 saved candidates per session; choose smaller sets to submit.");
            var snapshots = new List<AnalysisSnapshot>();
            foreach (var file in chosen)
            {
                var path = file.TryGetLocalPath() ?? throw new InvalidOperationException("Choose local workspace Markdown.");
                if (_tabs.Any(tab => tab.Session.Path is { } openPath && string.Equals(openPath, path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
                    throw new InvalidOperationException("This document is open; select its reviewed buffer instead of a separate saved copy.");
                var snapshot = await reader.ReadAsync(workspaceRoot, path);
                snapshots.Add(snapshot);
                if (snapshots.Sum(source => (long)source.Text.Length) > 200_000) throw new InvalidOperationException("Saved selection exceeds 200,000 characters.");
            }
            foreach (var snapshot in snapshots)
            {
                var path = chosen[snapshots.IndexOf(snapshot)].TryGetLocalPath()!;
                savedPaths[snapshot.ReferenceKey] = path;
                savedSnapshots[snapshot.ReferenceKey] = snapshot;
                var identity = DocumentIdentityPolicy.FindCanonical(snapshot.Text);
                classifications[snapshot.ReferenceKey] = identity is { } id ? _classificationCatalog?.Find(id)?.Classification : null;
            }
            return snapshots;
        }
        async Task<bool> VerifyFreshAsync(IReadOnlyList<AnalysisSnapshot> sources)
        {
            if (_activeWorkspace?.RootPath != workspaceRoot) return false;
            foreach (var source in sources)
            {
                if (!savedPaths.TryGetValue(source.ReferenceKey, out var path))
                {
                    if (!Fresh([source])) return false;
                    continue;
                }
                if (_tabs.Any(tab => tab.Session.Path == path)) return false;
                try { if ((await reader.ReadAsync(workspaceRoot!, path)).SourceHash != source.SourceHash) return false; }
                catch { return false; }
            }
            return true;
        }
        var selected = await new CpWorkspaceAnalysisWindow(_cpAnalysisClient, entries.Select(entry => entry.Snapshot).ToArray(), Fresh, _recovery as ISemanticDocumentResultStore, classifications,
                                                         workspaceRoot is null ? null : LoadSavedAsync, VerifyFreshAsync).ShowDialog<SemanticMatch?>(this);
        if (selected is null) return new(CommandOutcome.Completed, "Reviewed analysis closed; source retained.");
        if (savedPaths.TryGetValue(selected.Section.DocumentKey, out var savedPath))
        {
            var snapshot = savedSnapshots[selected.Section.DocumentKey];
            if (!await VerifyFreshAsync([snapshot])) return new(CommandOutcome.Unavailable, "Saved source changed; review it again.");
            await OpenDocumentAsync(savedPath);
            if (_session.Path != savedPath || !snapshot.Matches(Editor.Text ?? "")) return new(CommandOutcome.Unavailable, "Opened source differs from reviewed snapshot.");
            Editor.Select(selected.Section.Offset, selected.Section.Length);
            SetNavigationPosition(selected.Section.Offset);
            Editor.Focus();
            return new(CommandOutcome.Completed, "Opened reviewed saved source section without edits.");
        }
        var target = entries.First(entry => entry.Snapshot.ReferenceKey == selected.Section.DocumentKey);
        if (!Fresh(new[] { target.Snapshot })) return new(CommandOutcome.Unavailable, "Source changed; reopen reviewed analysis.");
        await SelectTabAsync(target.Tab);
        if (_activeTab != target.Tab) return new(CommandOutcome.Cancelled, "Source navigation cancelled.");
        Editor.Select(selected.Section.Offset, selected.Section.Length);
        SetNavigationPosition(selected.Section.Offset);
        Editor.Focus();
        return new(CommandOutcome.Completed, "Opened semantic source section; no relationship or source edits.");
    }

    private async void CpAnalysis_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs)
        => await ExecuteCommandAsync("document.cpAnalysis");

    private async Task<CommandResult> ShowCpAnalysisAsync()
    {
        var draftKey = _session.DraftKey;
        var snapshot = AnalysisSnapshot.Capture("current-document", Editor.Text ?? "");
        var window = new CpAnalysisWindow(snapshot
                                       , () => _session.DraftKey == draftKey && snapshot.Matches(Editor.Text ?? "")
                                       , () => _cpAnalysisClient
                                       , candidate => { _cpAnalysisClient?.Dispose(); _cpAnalysisClient = candidate; });
        await window.ShowDialog(this);
        return new(CommandOutcome.Completed, "CP analysis screen closed; source preserved.");
    }
}
