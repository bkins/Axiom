using Axiom.Core;

namespace Axiom.App;

public partial class MainWindow
{
    private async void CommandPalette_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs) => await ShowCommandPaletteAsync();

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

    private CommandContext CaptureCommandContext() => new(_session.DraftKey, _activeWorkspace?.WorkspaceId, _session.Path,
        _sessionTransitionRunning || _navigationRunning || _metadataApplying, IsEditorCommandTarget(), Editor.CanUndo, Editor.CanRedo,
        _mutationBlocked, true);

    private void InitializeCommands()
    {
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
        RegisterCommand("document.new", "New unsaved document", "Ctrl+N", false, async (_, _) =>
        {
            if (!await ResolvePendingMetadataAsync()) return new(CommandOutcome.Cancelled, "New document cancelled.");
            _initialPlaceholder = false;
            await LoadSessionAsync(_documents.NewDocument(), "New unsaved document");
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
                                , Func<CommandContext, CommandAvailability>? availability = null) =>
        _commands.Register(new(id, title, shortcut, mutatesFiles, availability ?? (_ => CommandAvailability.Available), execute));

    internal async Task<CommandResult> ExecuteCommandAsync(string id, CancellationToken cancellationToken = default)
    {
        var result = await _commands.ExecuteAsync(id, CaptureCommandContext(), cancellationToken);
        if (result.Outcome == CommandOutcome.Failed && result.Error is { } error) ReportError("command.failed." + id, error, _session.Path);
        else if (result.Outcome == CommandOutcome.Unavailable) ShowInformation(result.Message);
        return result;
    }
}
