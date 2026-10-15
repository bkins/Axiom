using Avalonia.Interactivity;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

public partial class MainWindow
{
    private async void NewWorkspace_OnClick(object? sender, RoutedEventArgs args)
        => await ExecuteCommandAsync("workspace.create");

    private async Task<CommandResult> ShowNewWorkspaceAsync()
    {
        var result = await new NewWorkspaceWindow(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), CreateWorkspaceAsync)
            .ShowDialog<CommandResult?>(this);
        return result ?? new(CommandOutcome.Cancelled, "Workspace creation cancelled.");
    }

    internal async Task<CommandResult> CreateWorkspaceAsync(string parent, string name)
    {
        if (_sessionTransitionRunning || _navigationRunning || _metadataApplying || _mutationBlocked)
            return new(CommandOutcome.Unavailable, "Finish the pending document operation before creating a workspace.");
        var path = WorkspaceCreationService.Create(parent, name);
        var opened = await SwitchWorkspaceAsync(path);
        var result = new CommandResult(opened ? CommandOutcome.Completed : CommandOutcome.Cancelled,
            opened ? $"Created and opened workspace: {path}" : $"Created folder: {path}. Workspace switch cancelled; current documents retained.");
        UpdateChrome(result.Message);
        return result;
    }
}
