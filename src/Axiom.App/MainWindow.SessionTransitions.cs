namespace Axiom.App;

public partial class MainWindow
{
    private bool _sessionTransitionRunning;

    private async Task<bool> RunSessionTransitionAsync(Func<Task> continuation)
    {
        if (_sessionTransitionRunning || _navigationRunning) return false;
        _sessionTransitionRunning = true;
        SetSessionCommandsEnabled(false);
        try
        {
            if (!await ResolvePendingMetadataAsync() || !await ConfirmAllTabsAsync() || !await PersistCleanSessionAsync()) return false;
            if (_tabs.Any(tab => tab.Session.IsDirty)) return false;
            await continuation();
            return true;
        }
        catch (Exception exception)
        {
            ReportError("session.transition.failed", exception, _folder);
            return false;
        }
        finally
        {
            _sessionTransitionRunning = false;
            SetSessionCommandsEnabled(true);
        }
    }

    private void SetSessionCommandsEnabled(bool enabled)
    {
        Editor.IsEnabled = enabled;
        DocumentTabs.IsEnabled = enabled;
        CommandPanel.IsEnabled = enabled;
        FileTree.IsEnabled = enabled;
        PinnedDocuments.IsEnabled = enabled;
        WorkspaceSearchResults.IsEnabled = enabled;
    }

    internal Task<bool> RunSessionTransitionForTestingAsync(Func<Task> continuation) => RunSessionTransitionAsync(continuation);
}
