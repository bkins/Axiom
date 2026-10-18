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
    private FolderClipboardSnapshot? _copiedWorkspaceFolder;

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
        FileTree.SelectionChanged += (_, _) => UpdateSelectionCount();
        FileTree.AddHandler(KeyDownEvent, WorkspaceSelectionKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        foreach (var (id, title, action) in new[] { ("rename", "Rename…", "Rename"), ("copy", "Copy folder…", "Copy folder"), ("export", "Export…", "Export") })
            RegisterCommand("workspace.batch." + id, title, null, true, (_, _) => RunNextWorkspaceToolAsync(action),
                _ => new(_activeWorkspace is not null && SelectedWorkspaceItems().Length > 0 && (id != "copy" || SelectedWorkspaceItems() is [{ IsDocument: false }]), "Select workspace items; folder copy requires exactly one folder."), hiddenFromPalette: id == "copy");
        RegisterCommand("workspace.copyPath", "Copy path", null, false, async (context, _) =>
        {
            if (Clipboard is null || ExplorerCommandPath(context) is not { } path) return new(CommandOutcome.Unavailable, "Select a saved workspace item first.");
            await Clipboard.SetTextAsync(path);
            return new(CommandOutcome.Completed, "Workspace item path copied.");
        }, context => new(ExplorerCommandNode(context) is not null, "Select a saved workspace item first."));
        RegisterCommand("workspace.showInExplorer", "Show in Explorer", null, false, async (context, _) =>
        {
            var path = ExplorerCommandPath(context);
            if (!OperatingSystem.IsWindows() || path is null || !File.Exists(path) && !Directory.Exists(path))
                return new CommandResult(CommandOutcome.Unavailable, "Select an existing workspace item on Windows.");
            await WindowsExplorerReveal.ShowAsync(path);
            return new CommandResult(CommandOutcome.Completed, "Workspace item shown in Explorer.");
        }, context => new(OperatingSystem.IsWindows() && ExplorerCommandNode(context) is not null, "Select a saved workspace item on Windows."));
        RegisterCommand("workspace.showFolderInExplorer", "Show workspace in Explorer", null, false, async (context, _) =>
        {
            if (!OperatingSystem.IsWindows() || _folder is null || _activeWorkspace?.WorkspaceId != context.WorkspaceId)
                return new(CommandOutcome.Unavailable, "Open a workspace on Windows first.");
            await WindowsExplorerReveal.ShowAsync(_folder);
            return new(CommandOutcome.Completed, "Workspace folder shown in Explorer.");
        }, context => new(OperatingSystem.IsWindows() && context.WorkspaceId is not null, "Open a workspace on Windows first."));
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
        RegisterCommand("workspace.copyFile", "Copy", null, false, CopyWorkspaceItemAsync,
            context => new(ExplorerCommandNode(context) is not null && SelectedWorkspaceItems().Length <= 1, "Choose one file or folder."));
        RegisterCommand("workspace.pasteFile", "Paste", null, true, PasteWorkspaceItemAsync,
            context => new(context.WorkspaceId == _activeWorkspace?.WorkspaceId && _activeWorkspace is not null &&
                (_copiedWorkspaceDocument is not null || _copiedWorkspaceFolder is not null), "Copy a file or folder in Axiom first."));
    }

    private async Task<CommandResult> CopyWorkspaceItemAsync(CommandContext context, CancellationToken cancellation)
    {
        var path = ExplorerCommandPath(context);
        var node = ExplorerCommandNode(context);
        if (path is null || node is null || _activeWorkspace is null || ManagementState is null || SelectedWorkspaceItems().Length > 1)
            return new(CommandOutcome.Unavailable, "Choose a file or folder.");
        if (!node.IsDocument)
        {
            var snapshot = await new WorkspaceFolderClipboard(ManagementState).CaptureAsync(_activeWorkspace, node.RelativePath, cancellation);
            _copiedWorkspaceFolder = snapshot;
            _copiedWorkspaceDocument = null;
        }
        else
        {
            WorkspaceFileOperations.ValidateAncestors(_activeWorkspace.RootPath, path);
            var bytes = await new WorkspaceImportService(new WorkspaceFileOperations(ManagementState)).ReviewAsync(path, false, cancellation);
            _copiedWorkspaceDocument = bytes;
            _copiedWorkspaceFolder = null;
        }
        _copiedWorkspaceName = Path.GetFileName(path);
        UpdateChrome("Copied saved content. Unsaved edits are not included.");
        return new(CommandOutcome.Completed, "Saved snapshot copied for Paste.");
    }

    private async Task<CommandResult> PasteWorkspaceItemAsync(CommandContext context, CancellationToken cancellation)
    {
        var bytes = _copiedWorkspaceDocument;
        var snapshot = _copiedWorkspaceFolder;
        var name = _copiedWorkspaceName;
        if (_activeWorkspace is not { } workspace || context.WorkspaceId != workspace.WorkspaceId || ManagementState is null ||
            name is null || bytes is null && snapshot is null || _workspaceBatchRunning)
            return new(CommandOutcome.Unavailable, "Copy a file or folder first and finish the current operation.");
        _workspaceBatchRunning = true;
        try
        {
            var node = ExplorerCommandNode(context);
            var folder = node is null ? "" : node.IsDocument ? Path.GetDirectoryName(node.RelativePath) ?? "" : node.RelativePath;
            var relative = await TextEntryDialog.ShowAsync(this, "Paste", "Choose a new location in this workspace. Existing files and folders are not replaced.", Path.Combine(folder, name));
            if (relative is null) return new(CommandOutcome.Cancelled, "Paste cancelled.");
            if (snapshot is not null)
            {
                var service = new WorkspaceFolderClipboard(ManagementState);
                var review = service.Review(snapshot, workspace, relative);
                var message = $"To: {review.Destination}\n{review.Files.Count} files; {review.Directories.Count} folders.\nCopied files get fresh identities; internal links follow the copy.\n\n" + string.Join("\n", review.Warnings.Distinct());
                if (await ChoiceDialog.ShowAsync(this, "Paste folder", message, "Paste", "Cancel") != "Paste") return new(CommandOutcome.Cancelled, "Paste cancelled.");
                await service.ApplyAsync(review, cancellation);
            }
            else
            {
                var destination = WorkspaceMutationPolicy.Resolve(workspace.RootPath, relative);
                if (await ChoiceDialog.ShowAsync(this, "Paste file", "Create the saved copy at " + destination + "?", "Paste", "Cancel") != "Paste") return new(CommandOutcome.Cancelled, "Paste cancelled.");
                await new WorkspaceImportService(new WorkspaceFileOperations(ManagementState, log: _log)).CopyReviewedAsync(workspace, relative, bytes!, false, cancellation);
            }
            await RefreshWorkspaceTreeAsync();
            return new(CommandOutcome.Completed, "Pasted the saved snapshot. Originals and unsaved edits retained.");
        }
        finally
        {
            _workspaceBatchRunning = false;
            _mutationBlocked = new ReviewedWorkspaceMove(ManagementState).Pending || (await ManagementState.GetPendingMutationsAsync()).Count > 0;
            RefreshWorkspaceActions();
        }
    }

    internal FolderClipboardSnapshot? FolderClipboardForTesting => _copiedWorkspaceFolder;

    private void Explorer_OnContextRequested(object? sender, ContextRequestedEventArgs args)
    {
        var control = args.Source as Control;
        var item = control as TreeViewItem ?? control?.GetVisualAncestors().OfType<TreeViewItem>().FirstOrDefault();
        var node = item?.DataContext as WorkspaceExplorerNode ?? FileTree.SelectedItem as WorkspaceExplorerNode;
        if (node is null || node.IsGroup || node.IsPlaceholder) return;
        if (!FileTree.SelectedItems.Contains(node)) FileTree.SelectedItem = node;
        if (SelectedWorkspaceItems().Length > 1)
        {
            var menu = new ContextMenu();
            foreach (var action in new[] { "Open", "Move", "Recycle" })
            {
                var entry = new MenuItem { Header = action + "…" };
                entry.Click += async (_, _) => await RunSelectedItemsAsync(action);
                menu.Items.Add(entry);
            }
            foreach (var action in new[] { "Rename", "Export" })
            {
                var entry = new MenuItem { Header = action + "…" };
                entry.Click += async (_, _) => await ExecuteCommandAsync("workspace.batch." + action.ToLowerInvariant()); menu.Items.Add(entry);
            }
            menu.Open(item ?? (Control)FileTree); args.Handled = true; return;
        }
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
        var ids = tab ? new[] { "document.close", "document.tabLeft", "document.tabRight", "workspace.manage.rename", "workspace.manage.move", "workspace.manage.duplicate", "workspace.pinItem", "workspace.copyFile", "workspace.copyPath", "workspace.showInExplorer" }
            : new[] { "document.new", "workspace.manage.createfolder", "workspace.manage.rename", "workspace.manage.move", "workspace.manage.duplicate", "workspace.manage.recycle", "workspace.batch.export", "workspace.pinItem", "workspace.copyFile", "workspace.pasteFile", "workspace.copyPath", "workspace.showInExplorer" };
        var menu = new ContextMenu();
        foreach (var id in ids)
        {
            var command = _commands.Commands.Single(command => command.Id == id);
            var availability = _commands.GetAvailability(id, context);
            if (id == "workspace.pinItem" && context.ExplorerIsFolder) continue;
            var item = new MenuItem { Header = WorkspaceActionTitle(id, context, command.Title), IsEnabled = availability.Enabled };
            if (!availability.Enabled) ToolTip.SetTip(item, availability.Reason);
            item.Click += async (_, _) => await ExecuteCommandInContextAsync(id, context);
            menu.Items.Add(item);
        }
        return menu;
    }

    private string WorkspaceActionTitle(string id, CommandContext context, string fallback) => id switch
    {
        "document.new" => "New file…",
        "workspace.create" => "New workspace…",
        "workspace.open" => "Open workspace…",
        "workspace.catalog" => "Switch workspace…",
        "workspace.refresh" => "Refresh",
        "workspace.createDocument" => "New from template…",
        "workspace.bulkEnroll" => "Enroll files…",
        "help.workspace" => "Help",
        "workspace.pinItem" => _workspacePins.Any(pin => pin.RelativePath.Equals(ExplorerCommandNode(context)?.RelativePath, WorkspacePathBoundary.Comparison)) ? "Unpin" : "Pin",
        _ => fallback
    };

    private async void WorkspaceToolbarToggle_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        _editorPreferences = _editorPreferences with { ShowWorkspaceActions = !_editorPreferences.ShowWorkspaceActions };
        RefreshWorkspaceActions();
        if (ManagementState is not null) await ManagementState.SetEditorPreferencesAsync(_editorPreferences, _appearance);
    }

    private static Button CreateFileActionButton(string id, string title)
    {
        var icon = id.Split('.').Last().ToLowerInvariant() switch
        {
            "open" => "open", "new" => "new", "createfolder" => "folder-new",
            "rename" => "edit", "move" => "move", "recycle" => "recycle",
            "pinitem" => title == "Unpin" ? "unpin" : "pin",
            "copyfile" or "copy" => "copy", "pastefile" => "paste", "export" => "export",
            _ => "more"
        };
        var button = new Button { Content = CreateWorkbenchIcon(icon), Width = 32, Height = 30,
            Margin = new Avalonia.Thickness(2), Tag = id };
        button.Classes.Add("toolbar-icon");
        Avalonia.Automation.AutomationProperties.SetName(button, title.TrimEnd('…'));
        ToolTip.SetTip(button, title.TrimEnd('…'));
        return button;
    }

    private void RefreshWorkspaceActions()
    {
        WorkspaceContextActions.IsVisible = _editorPreferences.ShowWorkspaceActions;
        WorkspaceToolbarToggle.Content = _editorPreferences.ShowWorkspaceActions ? "⌃" : "⌄";
        Avalonia.Automation.AutomationProperties.SetName(WorkspaceToolbarToggle, _editorPreferences.ShowWorkspaceActions ? "Hide file actions" : "Show file actions");
        ToolTip.SetTip(WorkspaceToolbarToggle, _editorPreferences.ShowWorkspaceActions ? "Hide file actions" : "Show file actions");
        WorkspaceContextActions.Children.Clear();
        if (!_editorPreferences.ShowWorkspaceActions || _activeWorkspace is null) return;
        if (SelectedWorkspaceItems().Length > 1)
        {
            foreach (var action in new[] { "Open", "Move", "Recycle", "Rename", "Export" })
            {
                var button = CreateFileActionButton(action, action);
                button.Click += async (_, _) => { if (action is "Rename" or "Export") await ExecuteCommandAsync("workspace.batch." + action.ToLowerInvariant()); else await RunSelectedItemsAsync(action); };
                WorkspaceContextActions.Children.Add(button);
            }
            return;
        }
        var context = CaptureCommandContext();
        if (SelectedWorkspaceItems().Length == 1)
        {
            var open = CreateFileActionButton("Open", "Open");
            open.Click += async (_, _) => await RunSelectedItemsAsync("Open");
            WorkspaceContextActions.Children.Add(open);
        }
        foreach (var id in new[] { "document.new", "workspace.manage.createfolder", "workspace.manage.rename", "workspace.manage.move", "workspace.pinItem", "workspace.copyFile", "workspace.pasteFile", "workspace.batch.export" })
        {
            if (!_commands.Commands.Any(command => command.Id == id) || !_commands.GetAvailability(id, context).Enabled) continue;
            var command = _commands.Commands.Single(command => command.Id == id);
            var button = CreateFileActionButton(id, WorkspaceActionTitle(id, context, command.Title));
            button.Click += async (_, _) => { await ExecuteCommandInContextAsync(id, context); RefreshWorkspaceActions(); };
            WorkspaceContextActions.Children.Add(button);
        }
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
