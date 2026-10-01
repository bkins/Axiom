using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

public partial class MainWindow
{
    private async void WorkspaceGit_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs) => await ExecuteCommandAsync("workspace.git");

    private async Task<CommandResult> ShowWorkspaceGitAsync()
    {
        if (_activeWorkspace is not { } workspace) return new(CommandOutcome.Unavailable, "Open a workspace to inspect Git.");
        var service = new GitWorkspaceService();
        var snapshot = await service.InspectAsync(workspace.RootPath);
        if (snapshot is null) return new(CommandOutcome.Unavailable, "This workspace has no Git repository. Editing and recovery remain available; Git setup is deliberate and optional.");
        bool Dirty(string relative)
        {
            var path = Path.GetFullPath(Path.Combine(snapshot.RepositoryRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            return FindTab(path) is { } tab && (tab.Session.IsDirty || tab.Session.Conflict is not null);
        }
        await new WorkspaceGitWindow(service, snapshot, Dirty).ShowDialog(this);
        return new(CommandOutcome.Completed, "Workspace Git closed.");
    }
}
