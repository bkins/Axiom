using Axiom.Core;

namespace Axiom.App;

public partial class MainWindow
{
    private void InitializeAdditionalCommands()
    {
        RegisterCommand("application.settings", "Configure editor settings", null, false, (_, _) => ConfigureSettingsAsync());
        RegisterCommand("application.shortcuts", "Configure keyboard shortcuts", null, false, (_, _) => ConfigureShortcutsAsync());
        RegisterCommand("application.about", "About Axiom", null, false, (_, _) => ShowAboutAsync());
        RegisterCommand("application.diagnostics", "Inspect diagnostics", null, false, (_, _) => ShowDiagnosticsAsync());
        RegisterCommand("application.maintenance", "Inspect state and export verified backup", null, true, (_, _) => ShowMaintenanceAsync());
        RegisterCommand("workspace.open", "Open Markdown workspace", null, false, (_, _) => OpenWorkspacePickerAsync());
        RegisterCommand("view.scrollSync", "Toggle synchronized preview scrolling", null, false, (_, _) =>
        {
            ToggleScrollSynchronization();
            return Task.FromResult(new CommandResult(CommandOutcome.Completed, "Scroll synchronization changed."));
        });
        RegisterCommand("view.previewAtCaret", "Show preview at editor caret", null, false, async (_, _) =>
        {
            await ScrollPreviewToSourceAsync(Editor.CaretOffset);
            return new(CommandOutcome.Completed, "Preview position reviewed.");
        });
        foreach (var extension in new[] { "docx", "html", "txt", "pdf" })
            RegisterCommand("document.export." + extension, "Export current buffer to " + extension.ToUpperInvariant(), null, true,
                (_, cancellation) => ExportCurrentBufferAsync(extension, cancellation));
        foreach (var kind in new[] { DocumentMutationKind.CreateFolder, DocumentMutationKind.Rename, DocumentMutationKind.Move, DocumentMutationKind.Duplicate, DocumentMutationKind.Recycle })
            RegisterCommand("workspace.manage." + kind.ToString().ToLowerInvariant(), kind + " workspace document", null, true,
                (_, cancellation) => ManageDocumentAsync(kind, cancellation), WorkspaceCommandAvailability);
        foreach (var kind in Enum.GetValues<MarkdownEditKind>())
            RegisterCommand("editor.format." + kind.ToString().ToLowerInvariant(), "Format " + GetMarkdownEditLabel(kind), null, false,
                (_, _) => Task.FromResult(new CommandResult(ApplyMarkdownEdit(kind) ? CommandOutcome.Completed : CommandOutcome.Cancelled, "Formatting reviewed.")));
        RegisterCommand("document.close", "Close current document tab", null, false, async (_, _) =>
            new(await CloseTabAsync(_activeTab) ? CommandOutcome.Completed : CommandOutcome.Cancelled, "Close tab workflow finished."));
        RegisterCommand("navigation.back", "Navigate back", null, false, async (_, _) =>
        {
            await NavigateHistoryAsync(false);
            return new(CommandOutcome.Completed, "Back navigation reviewed.");
        }, _ => new(_backLocations.Count > 0, _backLocations.Count > 0 ? null : "No previous document location."));
        RegisterCommand("navigation.forward", "Navigate forward", null, false, async (_, _) =>
        {
            await NavigateHistoryAsync(true);
            return new(CommandOutcome.Completed, "Forward navigation reviewed.");
        }, _ => new(_forwardLocations.Count > 0, _forwardLocations.Count > 0 ? null : "No forward document location."));
        RegisterCommand("navigation.followLink", "Follow Markdown link at caret", null, false, async (_, _) =>
        {
            await FollowEditorLinkAsync();
            return new(CommandOutcome.Completed, "Link navigation reviewed.");
        }, _ => new(GetCurrentStructure().FindLinkAt(Editor.CaretOffset) is not null, "Place the caret in a Markdown link."));
        RegisterCommand("workspace.refresh", "Refresh workspace", null, false, async (_, _) =>
        {
            await RefreshWorkspaceSafelyAsync();
            return new(CommandOutcome.Completed, "Workspace refresh reviewed.");
        }, WorkspaceCommandAvailability);
    }

    private CommandAvailability WorkspaceCommandAvailability(CommandContext context) =>
        new(context.WorkspaceId is not null, context.WorkspaceId is null ? "Open a workspace first." : null);
}
