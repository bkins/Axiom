using Axiom.Core;
using Axiom.Infrastructure;

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
        var entries = sourceTabs.Select((tab, index) => (Tab: tab, Snapshot: AnalysisSnapshot.Capture("document-" + (index + 1) + "-" + Path.GetFileName(tab.Session.Path ?? "unsaved"), tab == _activeTab ? Editor.Text ?? "" : tab.Session.Text))).ToArray();
        bool Fresh(IReadOnlyList<AnalysisSnapshot> selected) => selected.All(source => entries.Any(entry => entry.Snapshot.ReferenceKey == source.ReferenceKey
            && _tabs.Contains(entry.Tab) && entry.Tab.Session.Conflict is null
            && source.Matches(entry.Tab == _activeTab ? Editor.Text ?? "" : entry.Tab.Session.Text)));
        var classifications = entries.ToDictionary(entry => entry.Snapshot.ReferenceKey, entry => entry.Tab.Session.DocumentId is { } id ? _classificationCatalog?.Find(id)?.Classification : null, StringComparer.Ordinal);
        var selected = await new CpWorkspaceAnalysisWindow(_cpAnalysisClient, entries.Select(entry => entry.Snapshot).ToArray(), Fresh, _recovery as ISemanticDocumentResultStore, classifications).ShowDialog<SemanticMatch?>(this);
        if (selected is null) return new(CommandOutcome.Completed, "Reviewed analysis closed; source retained.");
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
