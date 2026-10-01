using Avalonia.Interactivity;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

public partial class MainWindow
{
    private async void DocumentHistory_OnClick(object? sender, RoutedEventArgs eventArgs) => await ExecuteCommandAsync("document.history");
    private async void HistorySetup_OnClick(object? sender, RoutedEventArgs eventArgs) => await ExecuteCommandAsync("workspace.historySetup");
    private async void HistoryCheckpoint_OnClick(object? sender, RoutedEventArgs eventArgs) => await ExecuteCommandAsync("document.checkpoint");

    private async Task<CommandResult> InitializeHistoryAsync()
    {
        var root = _activeWorkspace?.RootPath;
        if (root is null) return new(CommandOutcome.Unavailable, "Open a workspace before setting up Git history.");
        var choice = await ChoiceDialog.ShowAsync(this, "Set up Git document history",
            $"Create a local Git repository in {root}? Only .git is created. No documents are committed automatically; use a deliberate selected-document checkpoint. No remote is configured.", "Create Git repository", "Cancel");
        if (choice != "Create Git repository") return new(CommandOutcome.Cancelled, "Git setup cancelled.");
        if (_activeWorkspace?.RootPath != root) return new(CommandOutcome.Cancelled, "Workspace changed before Git setup.");
        await new GitDocumentHistoryService().InitializeAsync(root);
        UpdateChrome("Local Git history initialized. Save and checkpoint individual documents deliberately.");
        return new(CommandOutcome.Completed, "Local Git repository created.");
    }

    private async Task<CommandResult> CheckpointDocumentAsync()
    {
        var tab = _activeTab;
        var path = tab.Session.Path;
        if (path is null || tab.Session.IsDirty || tab.Session.Conflict is not null)
            return new(CommandOutcome.Unavailable, "Save the document and resolve conflicts before checkpointing its saved bytes.");
        var service = new GitDocumentHistoryService();
        var location = await service.LocateAsync(path);
        if (location is null) return new(CommandOutcome.Unavailable, "Set up local Git history in the workspace before checkpointing.");
        var bytes = await service.ReviewCheckpointAsync(location);
        var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        var choice = await ChoiceDialog.ShowAsync(this, "Checkpoint saved document",
            $"Record only saved {location.RelativePath} ({bytes.Length:N0} bytes; SHA-256 {digest}) in {location.RepositoryRoot}? Axiom writes a Git checkpoint reference, preserving your branch, HEAD, index and unrelated files. Save does not checkpoint automatically.", "Checkpoint", "Cancel");
        if (choice != "Checkpoint") return new(CommandOutcome.Cancelled, "Checkpoint cancelled.");
        if (_activeTab != tab || tab.Session.Path != path || tab.Session.IsDirty || tab.Session.Conflict is not null)
            return new(CommandOutcome.Cancelled, "Document changed after review; checkpoint was not created.");
        var commit = await service.CheckpointAsync(location, bytes);
        UpdateChrome("Git checkpoint created: " + commit[..12]);
        return new(CommandOutcome.Completed, "Checkpoint created: " + commit);
    }

    private async Task<CommandResult> ShowDocumentHistoryAsync()
    {
        var tab = _activeTab;
        var path = tab.Session.Path;
        if (path is null) return new(CommandOutcome.Unavailable, "Save the document before browsing Git history.");
        var service = new GitDocumentHistoryService();
        var location = await service.LocateAsync(path);
        if (location is null) return new(CommandOutcome.Unavailable, "This document has no Git repository. Use File → Set Up Git History for deliberate local setup. Recovery and Undo remain available.");
        var revisions = await service.ListAsync(location);
        if (revisions.Count == 0) return new(CommandOutcome.Unavailable, "No committed Git history exists for this document. Save does not create Git commits.");
        if (_activeTab != tab || tab.Session.Path != path) return new(CommandOutcome.Cancelled, "Document changed while history loaded.");
        var selected = await new DocumentHistoryWindow(service, location, revisions, tab.Session.Text, tab.Session.SavedText, tab.Session.Conflict?.ExternalText)
            .ShowDialog<HistoricalRestoreRequest?>(this);
        if (selected is null) return new(CommandOutcome.Completed, "History closed.");
        if (_activeTab != tab || tab.Session.Path != path) return new(CommandOutcome.Cancelled, "Document changed before restore.");
        if (tab.Session.Conflict is not null) return new(CommandOutcome.Unavailable, "Resolve the external-change conflict before restoring a revision.");
        var choice = await ChoiceDialog.ShowAsync(this, "Restore historical revision",
            $"Restore {selected.Revision.CommitHash[..12]} ({selected.Revision.Path}) into {tab.Label}? The current document identity is preserved. Disk is unchanged until Save.", "Restore", "Cancel");
        if (choice != "Restore") return new(CommandOutcome.Cancelled, "Restore cancelled.");
        if (!await ResolvePendingMetadataAsync() || !await ConfirmSafeToReplaceSessionAsync()) return new(CommandOutcome.Cancelled, "Restore cancelled; current buffer retained.");
        if (_activeTab != tab || tab.Session.Path != path) return new(CommandOutcome.Cancelled, "Document path changed while resolving unsaved work; restore was not applied.");
        await RestoreHistoricalBufferAsync(selected.Text);
        return new(CommandOutcome.Completed, "Historical revision restored into protected buffer. Review and Save deliberately.");
    }

    internal async Task RestoreHistoricalBufferAsync(string historical)
    {
        var tab = _activeTab;
        if (tab.Session.IsDirty) throw new IOException("Resolve unsaved work before restoring a historical revision.");
        if (tab.Session.Conflict is not null) throw new IOException("Resolve the external-change conflict before restoring a revision.");
        var original = tab.Session.Text;
        var originalPath = tab.Session.Path;
        var originalSaved = tab.Session.SavedText;
        var originalWriteUtc = tab.Session.LastKnownWriteUtc;
        var restored = new RevisionRestoreService().Prepare(original, historical);
        var staged = DocumentSession.Recover(new(tab.Session.DraftKey, tab.Session.Path, restored, DateTimeOffset.UtcNow,
            tab.Session.LastKnownWriteUtc, tab.Session.IsNew), tab.Session.SavedText);
        await _recoveryGate.WaitAsync();
        try
        {
            await _documents.ProtectAsync(staged);
            if (_activeTab != tab || tab.Session.Text != original || tab.Session.Path != originalPath ||
                tab.Session.SavedText != originalSaved || tab.Session.LastKnownWriteUtc != originalWriteUtc)
            {
                await _documents.ProtectAsync(tab.Session);
                throw new IOException("The buffer changed while restore protection was being written. Restore was not applied.");
            }
            using (Editor.Document.RunUpdate()) Editor.Document.Replace(0, Editor.Document.TextLength, restored);
            tab.LastProtectedText = tab.Session.IsDirty ? restored : null;
            tab.RecoveryStatus = tab.Session.IsDirty ? "Recovery protected" : "Recovery ready";
            UpdateChrome("Historical revision restored into protected buffer. Review and Save deliberately.");
        }
        finally { _recoveryGate.Release(); }
    }
}
