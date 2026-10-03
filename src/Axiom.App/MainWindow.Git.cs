using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

public partial class MainWindow
{
    private WorkspaceGitView? _gitView;
    private string? _gitWorkspaceRoot;
    private void ClearStaleGitPanel()
    {
        if (_gitWorkspaceRoot is null || WorkspacePathBoundary.Comparer.Equals(_gitWorkspaceRoot, _activeWorkspace?.RootPath)) return;
        _gitView?.CancelOperation();
        _gitView = null; _gitWorkspaceRoot = null;
        GitPanelHost.Content = new Avalonia.Controls.TextBlock { Text = "Workspace changed. Open Git to inspect this workspace.", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    }
    private async void WorkspaceGit_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs) => await ExecuteCommandAsync("workspace.git");

    private async Task<CommandResult> ShowWorkspaceGitAsync()
    {
        if (_gitView?.IsBusy == true) return new(CommandOutcome.Unavailable, "A Git operation is running; use Cancel operation in the Git panel or wait.");
        if (_activeWorkspace is not { } workspace) return new(CommandOutcome.Unavailable, "Open a workspace to inspect Git.");
        var service = new GitWorkspaceService();
        var snapshot = await service.InspectAsync(workspace.RootPath);
        if (snapshot is null) return new(CommandOutcome.Unavailable, "This workspace has no Git repository. Editing and recovery remain available; Git setup is deliberate and optional.");
        if (_activeWorkspace?.WorkspaceId != workspace.WorkspaceId) return new(CommandOutcome.Cancelled, "Workspace changed during Git inspection.");
        bool ContextValid() => _activeWorkspace?.WorkspaceId == workspace.WorkspaceId && !_sessionTransitionRunning && !_navigationRunning && !_metadataApplying && !_mutationBlocked;
        bool Dirty(string relative)
        {
            var path = Path.GetFullPath(Path.Combine(snapshot.RepositoryRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            return FindTab(path) is { } tab && (tab.Session.IsDirty || tab.Session.Conflict is not null);
        }
        _gitView?.CancelOperation();
        _gitWorkspaceRoot = workspace.RootPath;
        _gitView = new WorkspaceGitView(this, service, snapshot, Dirty, ContextValid, () => { SetToolWindowVisible(false); Editor.TextArea.Focus(); _ = PersistWorkbenchAsync(); });
        GitPanelHost.Content = _gitView;
        SidebarTopics.SelectedIndex = 6;
        _toolWidth = 480;
        SetToolWindowVisible(true);
        await PersistWorkbenchAsync();
        return new(CommandOutcome.Completed, "Embedded Git changes opened. Editing remains available; Git operates on explicitly reviewed saved files.");
    }
    internal WorkspaceGitView? GitViewForTesting => _gitView;
}
