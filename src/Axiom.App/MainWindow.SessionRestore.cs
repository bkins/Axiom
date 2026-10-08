using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

public partial class MainWindow
{
    private IWorkspaceSessionStore? SessionStore => SessionStoreForTesting ?? ManagementState;
    private readonly TaskCompletionSource _startupCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Task<bool> PersistCleanSessionAsync() => PersistSessionLocationsAsync(requireClean: true);

    private async Task<bool> PersistSessionLocationsAsync(bool requireClean)
    {
        if (_activeWorkspace is not { } workspace || SessionStore is not { } store) return true;
        if (requireClean && _tabs.Any(tab => tab.Session.IsDirty))
        {
            UpdateChrome("Resolve all dirty tabs before remembering the clean session.");
            return false;
        }
        try
        {
            CaptureEditorState(_activeTab);
            if (_previewReady)
            {
                var result = await Preview.InvokeScript("(() => { const root = document.scrollingElement || document.documentElement; const range = Math.max(0, root.scrollHeight - window.innerHeight); return range === 0 ? 0 : root.scrollTop / range; })()");
                if (TryParsePreviewScrollRatio(result, out var ratio)) _activeTab.PreviewScrollRatio = ratio;
            }
            var locations = _tabs.Where(tab => tab.Session.Path is { } path && IsWithinFolder(path, workspace.RootPath))
                                 .Select(tab => new WorkspaceDocumentLocation(Path.GetRelativePath(workspace.RootPath, tab.Session.Path!)
                                                                           , tab.CaretOffset
                                                                           , tab.SelectionStart
                                                                           , tab.SelectionLength
                                                                           , tab.HorizontalOffset
                                                                           , tab.VerticalOffset
                                                                           , tab.PreviewScrollRatio))
                                 .ToArray();
            var activePath = _activeTab.Session.Path;
            var activeRelative = activePath is not null && IsWithinFolder(activePath, workspace.RootPath)
                ? Path.GetRelativePath(workspace.RootPath, activePath) : null;
            await store.SaveWorkspaceSessionAsync(workspace.WorkspaceId, new WorkspaceDocumentSession(1, activeRelative, locations));
            return true;
        }
        catch (Exception exception)
        {
            ReportError("session.remember.failed", exception, workspace.RootPath);
            return false;
        }
    }

    private async Task RestoreCleanSessionAsync()
    {
        if (_activeWorkspace is not { } workspace || SessionStore is not { } store) return;
        var warnings = new List<string>();
        try
        {
            var remembered = await store.GetWorkspaceSessionAsync(workspace.WorkspaceId);
            if (remembered is null) return;
            var reader = new MarkdownLinkTargetReader(_files);
            foreach (var location in remembered.Documents)
            {
                try
                {
                    var path = WorkspaceMutationPolicy.Resolve(workspace.RootPath, location.RelativePath);
                    if (FindTab(path) is not null) continue;
                    var target = await reader.ReadAsync(workspace.RootPath, path);
                    if (target.Status != MarkdownLinkResolutionStatus.Resolved)
                    {
                        warnings.Add($"{location.RelativePath}: {target.Status}");
                        continue;
                    }
                    var session = await _documents.OpenAsync(path);
                    if (_initialPlaceholder && _tabs.Count == 1 && !_session.IsDirty && _session.IsNew)
                    {
                        _selectingTab = true;
                        _tabs.Clear();
                        _selectingTab = false;
                    }
                    _initialPlaceholder = false;
                    await LoadSessionAsync(session, "Restored saved Markdown without changing identity.");
                    if (FindTab(path) is not { } tab) continue;
                    tab.CaretOffset = Math.Clamp(location.CaretOffset, 0, tab.Document.TextLength);
                    tab.SelectionStart = Math.Clamp(location.SelectionStart, 0, tab.Document.TextLength);
                    tab.SelectionLength = Math.Clamp(location.SelectionLength, 0, tab.Document.TextLength - tab.SelectionStart);
                    tab.HorizontalOffset = SafeOffset(location.HorizontalOffset);
                    tab.VerticalOffset = SafeOffset(location.VerticalOffset);
                    tab.PreviewScrollRatio = Math.Clamp(SafeOffset(location.PreviewScrollRatio), 0, 1);
                    ActivateTab(tab, "Restored clean document position.");
                }
                catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
                {
                    warnings.Add($"{location.RelativePath}: {exception.Message}");
                }
            }
            var active = _tabs.FirstOrDefault(tab => tab.Session.Path is { } path &&
                Path.GetRelativePath(workspace.RootPath, path).Equals(remembered.ActiveRelativePath, WorkspacePathBoundary.Comparison));
            if (active is not null)
            {
                await SelectTabAsync(active);
                ActivateTab(active, "Restored clean document tabs from saved Markdown.");
            }
            if (warnings.Count > 0) UpdateChrome("Skipped remembered documents: " + string.Join("; ", warnings));
        }
        catch (Exception exception) { ReportError("session.restore.failed", exception, workspace.RootPath); }
    }

    private static double SafeOffset(double value) => double.IsFinite(value) ? Math.Max(0, value) : 0;

    internal IWorkspaceSessionStore? SessionStoreForTesting { get; set; }
    internal Task<bool> PersistCleanSessionForTestingAsync() => PersistCleanSessionAsync();
    internal Task RestoreCleanSessionForTestingAsync() => RestoreCleanSessionAsync();
    internal Task StartupCompleted => _startupCompleted.Task;
    internal async Task InitializeSessionForTestingAsync()
    {
        await InitializeManagementAsync();
        await RestoreDraftIfAvailableAsync();
    }
}
