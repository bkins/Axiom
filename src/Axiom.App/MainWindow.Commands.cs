using Axiom.Core;

namespace Axiom.App;

public partial class MainWindow
{
    private readonly CommandDispatcher _commands = new();

    private CommandContext CaptureCommandContext() => new(_session.DraftKey, _activeWorkspace?.WorkspaceId, _session.Path,
        _sessionTransitionRunning || _navigationRunning || _metadataApplying, IsEditorCommandTarget(), Editor.CanUndo, Editor.CanRedo,
        _mutationBlocked, true);

    private void InitializeCommands()
    {
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
