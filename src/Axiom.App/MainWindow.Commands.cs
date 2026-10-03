using Axiom.Core;

namespace Axiom.App;

public partial class MainWindow
{
    private async void CommandPalette_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs) => await ShowCommandPaletteAsync();
    internal Task ShowCommandPaletteForTestingAsync() => ShowCommandPaletteAsync();

    private async Task ShowCommandPaletteAsync()
    {
        if (_sessionTransitionRunning || _navigationRunning || _metadataApplying) return;
        var focus = FocusManager?.GetFocusedElement() as Avalonia.Input.InputElement;
        var context = CaptureCommandContext();
        var selected = await new CommandPaletteWindow(_commands, context, _shortcutBindings).ShowDialog<string?>(this);
        focus?.Focus();
        if (selected is not null) await ExecuteCommandAsync(selected);
    }

    private readonly CommandDispatcher _commands = new();
    internal IReadOnlyList<AxiomCommand> RegisteredCommandsForTesting => _commands.Commands;

    private CommandContext CaptureCommandContext() => new(_session.DraftKey, _activeWorkspace?.WorkspaceId, _session.Path,
        _sessionTransitionRunning || _navigationRunning || _metadataApplying, IsEditorCommandTarget(), Editor.CanUndo, Editor.CanRedo,
        _mutationBlocked, true)
    {
        ExplorerRelativePath = (FileTree.SelectedItem as WorkspaceExplorerNode) is { IsGroup: false, IsPlaceholder: false } node ? node.RelativePath : null
      , ExplorerIsFolder = FileTree.SelectedItem is WorkspaceExplorerNode { IsDocument: false, Heading: null, IsGroup: false, IsPlaceholder: false }
    };

    private void InitializeCommands()
    {
        InitializeAdditionalCommands();
        RegisterCommand("workspace.filter", "Show filename filter", "Ctrl+Shift+L", false, (_, _) =>
        {
            ExplorerFilterPanel.IsVisible = true;
            ExplorerFilter.Focus();
            return Task.FromResult(new CommandResult(CommandOutcome.Completed, "Filename filter shown; existing query retained."));
        });
        RegisterCommand("help.walkthrough", "Open captioned app walkthrough", null, false, (_, _) => ShowWalkthroughAsync());
        foreach (var panel in new[] { ("relationships", "Relationships", 0), ("metadata", "Metadata", 1), ("classification", "Classification", 2), ("search", "Workspace Search", 3), ("outline", "Document Outline", 4), ("backlinks", "Backlinks", 5) })
        {
            RegisterCommand("view." + panel.Item1, "Toggle " + panel.Item2 + " panel", panel.Item1 == "search" ? "Ctrl+Shift+F" : null, false, async (_, _) =>
            {
                ToggleToolWindow(panel.Item3);
                await PersistWorkbenchAsync();
                return new(CommandOutcome.Completed, panel.Item2 + " panel toggled.");
            });
        }
        RegisterCommand("view.resetLayout", "Reset workbench layout", null, false, async (_, _) =>
        {
            ApplyWorkbenchLayout(new());
            HelpPanelHost.IsVisible = false;
            await PersistWorkbenchAsync();
            return new(CommandOutcome.Completed, "Workbench layout reset; working buffers retained.");
        });
        RegisterCommand("document.cpAnalysis", "CP connection and reviewed document insights", null, false, (_, _) => ShowCpAnalysisAsync());
        RegisterCommand("workspace.cpAnalysis", "Reviewed semantic search and cross-document analysis", null, false, (_, _) => ShowCpWorkspaceAnalysisAsync());
        Closed += (_, _) => _cpAnalysisClient?.Dispose();
        RegisterCommand("application.install", "Install Axiom for current user", null, true, (_, _) => InstallCurrentUserAsync(),
            _ => new(OperatingSystem.IsWindows(), "Current-user installation requires Windows."));
        RegisterCommand("application.checkUpdate", "Check trusted update", null, true, (_, _) => CheckTrustedUpdateAsync(),
            _ => new(OperatingSystem.IsWindows(), "Verified Windows update packages require Windows."));
        RegisterCommand("workspace.git", "Inspect workspace Git", null, true, (_, _) => ShowWorkspaceGitAsync(),
            context => new(context.WorkspaceId is not null, context.WorkspaceId is null ? "Open a workspace before inspecting Git." : null));
        RegisterCommand("document.structure", "Browse document structure", null, false, (_, _) => ShowDocumentStructureAsync());
        RegisterCommand("workspace.deletedDocuments", "Recover deleted documents", null, true, (_, _) => ShowDeletedDocumentsAsync(),
            context => new(context.WorkspaceId is not null, context.WorkspaceId is null ? "Open a workspace before recovering documents." : null));
        RegisterCommand("document.history", "Browse document history", null, false, (_, _) => ShowDocumentHistoryAsync(),
            context => new(context.DocumentPath is not null, context.DocumentPath is null ? "Save a document before browsing history." : null));
        RegisterCommand("workspace.historySetup", "Set up local Git history", null, true, (_, _) => InitializeHistoryAsync(),
            context => new(context.WorkspaceId is not null, context.WorkspaceId is null ? "Open a workspace before setting up Git history." : null));
        RegisterCommand("document.checkpoint", "Checkpoint saved document", null, true, (_, _) => CheckpointDocumentAsync(),
            context => new(context.DocumentPath is not null && !_session.IsDirty && _session.Conflict is null,
                context.DocumentPath is null || _session.IsDirty || _session.Conflict is not null ? "Save the document and resolve conflicts first." : null));
        RegisterCommand("document.new", "New unsaved document", "Ctrl+N", false, async (context, _) =>
        {
            if (!await ResolvePendingMetadataAsync()) return new(CommandOutcome.Cancelled, "New document cancelled.");
            await NewDocumentInSelectedFolderAsync(context.ExplorerRelativePath is null ? null : ExplorerCommandNode(context));
            return new(CommandOutcome.Completed, "New document opened.");
        });
        RegisterCommand("document.save", "Save document", "Ctrl+S", true, async (_, _) =>
            new(await SaveDocumentAsync(false) ? CommandOutcome.Completed : CommandOutcome.Cancelled, "Save workflow finished."));
        RegisterCommand("document.saveAs", "Save document as", "Ctrl+Shift+S", true, async (_, _) =>
            new(await SaveDocumentAsync(true) ? CommandOutcome.Completed : CommandOutcome.Cancelled, "Save As workflow finished."));
        RegisterCommand("editor.undo", "Undo", "Ctrl+Z", false, (_, _) =>
        {
            Editor.Undo();
            return Task.FromResult(new CommandResult(CommandOutcome.Completed, "Undone."));
        }, context => new(context.CanUndo, context.CanUndo ? null : "No editor action to undo."));
        RegisterCommand("editor.redo", "Redo", "Ctrl+Y", false, (_, _) =>
        {
            Editor.Redo();
            return Task.FromResult(new CommandResult(CommandOutcome.Completed, "Redone."));
        }, context => new(context.CanRedo, context.CanRedo ? null : "No editor action to redo."));
        RegisterCommand("view.reading", "Toggle reading mode", "Ctrl+Shift+R", false, async (_, _) =>
        {
            await ToggleReadingModeAsync();
            return new(CommandOutcome.Completed, "Reading mode changed.");
        });
        RegisterCommand("help.open", "Open contextual Help", "F1", false, async (_, _) =>
        {
            await ShowHelpAsync(HelpContext());
            return new(CommandOutcome.Completed, "Help opened.");
        });
        foreach (var topic in new[] { ("home", "Browse Help", (string?)null), ("metadata", "Metadata Help", "metadata"), ("workspace", "Workspace Help", "Workspace loading"), ("relationships", "Relationships Help", "Explicit relationships") })
        {
            RegisterCommand("help." + topic.Item1, topic.Item2, null, false, async (_, _) =>
            {
                await ShowHelpAsync(topic.Item3);
                return new(CommandOutcome.Completed, "Help opened.");
            });
        }
    }

    private void RegisterCommand(string id
                                , string title
                                , string? shortcut
                                , bool mutatesFiles
                                , Func<CommandContext, CancellationToken, Task<CommandResult>> execute
                                , Func<CommandContext, CommandAvailability>? availability = null
                                , bool isCancellationControl = false) =>
        _commands.Register(new(id, title, shortcut, mutatesFiles, availability ?? (_ => CommandAvailability.Available), execute, isCancellationControl));

    internal async Task<CommandResult> ExecuteCommandAsync(string id, CancellationToken cancellationToken = default)
        => await ExecuteCommandInContextAsync(id, CaptureCommandContext(), cancellationToken);

    private async Task<CommandResult> ExecuteCommandInContextAsync(string id, CommandContext context, CancellationToken cancellationToken = default)
    {
        var result = await _commands.ExecuteAsync(id, context, cancellationToken);
        if (result.Outcome == CommandOutcome.Failed && result.Error is { } error) ReportError("command.failed." + id, error, _session.Path);
        else if (result.Outcome == CommandOutcome.Unavailable) ShowInformation(result.Message);
        return result;
    }
}
