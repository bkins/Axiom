using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

public partial class MainWindow
{
    private byte[]? _copiedWorkspaceDocument;
    private string? _copiedWorkspaceName;

    private WorkspaceExplorerNode? ExplorerCommandNode(CommandContext context)
    {
        if (_activeWorkspace is null || context.WorkspaceId != _activeWorkspace.WorkspaceId) return null;
        if (context.ExplorerRelativePath is { } relative)
            return new WorkspaceExplorerNode(Path.GetFileName(relative), relative, !context.ExplorerIsFolder);
        if (_folder is not null && context.DocumentPath is { } path && IsWithinFolder(path, _folder))
        {
            relative = Path.GetRelativePath(_folder, path);
            return new WorkspaceExplorerNode(Path.GetFileName(relative), relative, true);
        }
        return null;
    }

    private string? ExplorerCommandPath(CommandContext context)
    {
        var node = ExplorerCommandNode(context);
        return _folder is null || node is null ? null : WorkspaceMutationPolicy.Resolve(_folder, node.RelativePath, node.IsDocument);
    }

    private void InitializeExplorerCommands()
    {
        RegisterCommand("workspace.copyPath", "Copy workspace item path", null, false, async (context, _) =>
        {
            if (Clipboard is null || ExplorerCommandPath(context) is not { } path) return new(CommandOutcome.Unavailable, "Select a saved workspace item first.");
            await Clipboard.SetTextAsync(path);
            return new(CommandOutcome.Completed, "Workspace item path copied.");
        }, context => new(ExplorerCommandNode(context) is not null, "Select a saved workspace item first."));
        RegisterCommand("workspace.showInExplorer", "Show workspace item in Explorer", null, false, async (context, _) =>
        {
            var path = ExplorerCommandPath(context);
            if (!OperatingSystem.IsWindows() || path is null || !File.Exists(path) && !Directory.Exists(path))
                return new CommandResult(CommandOutcome.Unavailable, "Select an existing workspace item on Windows.");
            await WindowsExplorerReveal.ShowAsync(path);
            return new CommandResult(CommandOutcome.Completed, "Workspace item shown in Explorer.");
        }, context => new(OperatingSystem.IsWindows() && ExplorerCommandNode(context) is not null, "Select a saved workspace item on Windows."));
        RegisterCommand("workspace.pinItem", "Pin or unpin selected workspace file", null, true, async (context, _) =>
        {
            var path = ExplorerCommandPath(context);
            if (_activeWorkspace is null || path is null || ExplorerCommandNode(context)?.IsDocument != true)
                return new(CommandOutcome.Unavailable, "Select a saved workspace file.");
            var relative = Path.GetRelativePath(_activeWorkspace.RootPath, path);
            if (_workspacePins.Any(pin => pin.RelativePath.Equals(relative, WorkspacePathBoundary.Comparison)))
                await _workspaces.UnpinAsync(_activeWorkspace, path);
            else await _workspaces.PinAsync(_activeWorkspace, path);
            await RefreshPinsAsync();
            return new(CommandOutcome.Completed, "Selected file pin updated.");
        }, context => new(ExplorerCommandNode(context)?.IsDocument == true, "Select a workspace file."));
        RegisterCommand("workspace.copyFile", "Copy saved workspace file for paste", null, false, async (context, cancellation) =>
        {
            var path = ExplorerCommandPath(context);
            if (path is null || _activeWorkspace is null || ManagementState is null || ExplorerCommandNode(context)?.IsDocument != true)
                return new(CommandOutcome.Unavailable, "Select a workspace file.");
            WorkspaceFileOperations.ValidateAncestors(_activeWorkspace.RootPath, path);
            _copiedWorkspaceDocument = await new WorkspaceImportService(new WorkspaceFileOperations(ManagementState)).ReviewAsync(path, false, cancellation);
            _copiedWorkspaceName = Path.GetFileName(path);
            UpdateChrome("Saved file copied for Paste file in Axiom. Unsaved editor changes are not included.");
            return new(CommandOutcome.Completed, "Saved file snapshot copied for Axiom paste.");
        }, context => new(ExplorerCommandNode(context)?.IsDocument == true, "Select a workspace file."));
        RegisterCommand("workspace.pasteFile", "Paste copied workspace file", null, true, async (context, cancellation) =>
        {
            if (_activeWorkspace is not { } workspace || context.WorkspaceId != workspace.WorkspaceId || ManagementState is null || _copiedWorkspaceDocument is null || _copiedWorkspaceName is null)
                return new(CommandOutcome.Unavailable, "Copy a saved file in Axiom first.");
            var node = ExplorerCommandNode(context);
            var folder = node is null ? "" : node.IsDocument ? Path.GetDirectoryName(node.RelativePath) ?? "" : node.RelativePath;
            var relative = await TextEntryDialog.ShowAsync(this, "Paste file", "Choose a workspace-relative destination. Existing targets are never overwritten. A copied Axiom document receives a distinct identity.", Path.Combine(folder, _copiedWorkspaceName));
            if (relative is null) return new(CommandOutcome.Cancelled, "File paste cancelled.");
            var destination = WorkspaceMutationPolicy.Resolve(workspace.RootPath, relative);
            if (await ChoiceDialog.ShowAsync(this, "Confirm file paste", "Create a copy of the saved snapshot at " + destination + "?", "Paste", "Cancel") != "Paste")
                return new(CommandOutcome.Cancelled, "File paste cancelled.");
            try
            {
                await new WorkspaceImportService(new WorkspaceFileOperations(ManagementState, log: _log)).CopyReviewedAsync(workspace, relative, _copiedWorkspaceDocument, false, cancellation);
                await RefreshWorkspaceTreeAsync();
                return new(CommandOutcome.Completed, "Saved file copied; existing tabs and files retained.");
            }
            finally { _mutationBlocked = (await ManagementState.GetPendingMutationsAsync()).Count > 0; }
        }, context => new(context.WorkspaceId == _activeWorkspace?.WorkspaceId && _activeWorkspace is not null && _copiedWorkspaceDocument is not null, "Copy a saved file in Axiom first."));
    }

    private void Explorer_OnContextRequested(object? sender, ContextRequestedEventArgs args)
    {
        var control = args.Source as Control;
        var item = control as TreeViewItem ?? control?.GetVisualAncestors().OfType<TreeViewItem>().FirstOrDefault();
        var node = item?.DataContext as WorkspaceExplorerNode ?? FileTree.SelectedItem as WorkspaceExplorerNode;
        if (node is null || node.IsGroup || node.IsPlaceholder) return;
        FileTree.SelectedItem = node;
        var context = CaptureCommandContext() with { ExplorerRelativePath = node.RelativePath, ExplorerIsFolder = !node.IsDocument && node.Heading is null };
        ShowWorkspaceContextMenu(item ?? (Control)FileTree, context, false);
        args.Handled = true;
    }

    private void Tab_OnContextRequested(object? sender, ContextRequestedEventArgs args)
    {
        if (sender is not Control { DataContext: DocumentTab tab } control) return;
        var context = CaptureCommandContext() with
        {
            DraftKey = tab.Session.DraftKey
          , DocumentPath = tab.Session.Path
          , ExplorerRelativePath = tab.Session.Path is { } path && _folder is not null && IsWithinFolder(path, _folder) ? Path.GetRelativePath(_folder, path) : null
          , ExplorerIsFolder = false
        };
        ShowWorkspaceContextMenu(control, context, true);
        args.Handled = true;
    }

    private void ShowWorkspaceContextMenu(Control target, CommandContext context, bool tab)
    {
        BuildWorkspaceContextMenu(context, tab).Open(target);
    }

    private ContextMenu BuildWorkspaceContextMenu(CommandContext context, bool tab)
    {
        var ids = tab ? new[] { "document.close", "workspace.manage.rename", "workspace.manage.move", "workspace.manage.duplicate", "workspace.pinItem", "workspace.copyFile", "workspace.copyPath", "workspace.showInExplorer" }
            : new[] { "document.new", "workspace.manage.createfolder", "workspace.manage.rename", "workspace.manage.move", "workspace.manage.duplicate", "workspace.manage.recycle", "workspace.pinItem", "workspace.copyFile", "workspace.pasteFile", "workspace.copyPath", "workspace.showInExplorer" };
        var menu = new ContextMenu();
        foreach (var id in ids)
        {
            var command = _commands.Commands.Single(command => command.Id == id);
            var availability = _commands.GetAvailability(id, context);
            var item = new MenuItem { Header = command.Title, IsEnabled = availability.Enabled };
            if (!availability.Enabled) ToolTip.SetTip(item, availability.Reason);
            item.Click += async (_, _) => await ExecuteCommandInContextAsync(id, context);
            menu.Items.Add(item);
        }
        return menu;
    }

    internal Task<CommandResult> ExecuteExplorerCommandForTestingAsync(string id, string relative, bool folder = false)
        => ExecuteCommandInContextAsync(id, CaptureCommandContext() with { ExplorerRelativePath = relative, ExplorerIsFolder = folder });

    internal ContextMenu WorkspaceContextMenuForTesting(string relative, bool folder, bool tab = false)
        => BuildWorkspaceContextMenu(CaptureCommandContext() with { ExplorerRelativePath = relative, ExplorerIsFolder = folder }, tab);

    internal async Task<CommandResult> MoveExplorerItemForTestingAsync(string source, string destination, bool folder)
    {
        _requestedMoveDestination = destination;
        try { return await ExecuteExplorerCommandForTestingAsync("workspace.manage.move", source, folder); }
        finally { _requestedMoveDestination = null; }
    }
}
