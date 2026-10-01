using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

public partial class MainWindow
{
    private async void DeletedDocuments_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs) => await ExecuteCommandAsync("workspace.deletedDocuments");

    private async Task<CommandResult> ShowDeletedDocumentsAsync()
    {
        if (_activeWorkspace is not { } workspace || ManagementState is not { } state)
            return new(CommandOutcome.Unavailable, "Open a workspace to browse deleted documents.");
        var items = await state.GetDeletedDocumentsAsync(workspace.WorkspaceId);
        var selected = await new DeletedDocumentsWindow(items).ShowDialog<DeletedDocument?>(this);
        if (selected is null) return new(CommandOutcome.Completed, "Deleted document recovery closed.");
        var relative = Path.GetRelativePath(workspace.RootPath, selected.Intent.SourcePath!);
        var destination = await TextEntryDialog.ShowAsync(this, "Restore deleted document", "Choose a workspace-relative Markdown path. Missing parent folders are reconstructed. An existing file is never overwritten.", relative);
        if (destination is null) return new(CommandOutcome.Cancelled, "Restore cancelled.");
        var choice = await ChoiceDialog.ShowAsync(this, "Confirm document recovery",
            "Restore the captured bytes? Preserve identity to reconnect retained metadata and relationships. A new identity leaves original relationships unresolved and does not copy metadata. Current metadata is never replaced by its historical snapshot.", "Preserve identity", "New identity", "Cancel");
        if (choice == "Cancel" || choice is null) return new(CommandOutcome.Cancelled, "Restore cancelled.");
        if (_activeWorkspace?.WorkspaceId != workspace.WorkspaceId) return new(CommandOutcome.Cancelled, "Workspace changed; restore cancelled.");
        var path = await new WorkspaceFileOperations(state, log: _log).RestoreAsync(workspace, selected.Intent.OperationId, destination, choice == "New identity");
        await LoadFolderAsync(workspace.RootPath);
        UpdateChrome("Restored " + path);
        return new(CommandOutcome.Completed, "Document restored: " + path);
    }
}
