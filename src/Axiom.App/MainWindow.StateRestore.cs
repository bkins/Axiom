using System.Diagnostics;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

public partial class MainWindow
{
    private async void RestoreState_OnClick(object? sender, RoutedEventArgs args) => await ExecuteCommandAsync("application.restoreState");

    private async Task<CommandResult> RestoreStateAsync()
    {
        if (_mutationBlocked || _sessionTransitionRunning || _navigationRunning || _workspaceBatchRunning)
            return new(CommandOutcome.Unavailable, "Resolve pending operations before restoring state.");
        if (!await ResolvePendingMetadataAsync()) return new(CommandOutcome.Cancelled, "Restore cancelled.");
        if (_tabs.Any(tab => tab.Session.IsDirty || tab.Session.Conflict is not null))
        {
            await ChoiceDialog.ShowAsync(this, "Save open documents first", "Save your open documents and resolve conflicts before restoring app state. Current protected drafts are retained in the automatic pre-restore backup, but are not merged into the restored state. Markdown files are not part of a state backup.", "OK");
            return new(CommandOutcome.Cancelled, "Unsaved work retained; restore not started.");
        }
        var selected = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose exported Axiom state backup", AllowMultiple = false });
        if (selected.FirstOrDefault()?.TryGetLocalPath() is not { } backup) return new(CommandOutcome.Cancelled, "Restore cancelled.");
        return await ReviewStateRestoreAsync(backup);
    }

    internal Task<CommandResult> RestoreBackupForTestingAsync(string backup) => ReviewStateRestoreAsync(backup);

    private async Task<CommandResult> ReviewStateRestoreAsync(string backup)
    {
        StateRestoreReview? review = null;
        var started = false;
        try
        {
            review = await new StateRestoreService(_operationalPaths).ReviewAsync(backup);
            var info = review.Inspection;
            if (await ChoiceDialog.ShowAsync(this, "Review state backup restoration", $"Backup: {backup}\nModified: {File.GetLastWriteTime(backup):g}\nSchema: {info.SchemaVersion}\nWorkspaces: {info.Workspaces}\nProtected drafts: {info.Drafts}\nIdentity paths: {info.Identities}\nIntegrity: {info.Integrity}\n\nAxiom will close, preserve a verified backup of current state, replace app state and restart. Existing state is replaced, not merged. This does not restore or modify Markdown files. Previously remembered paths may refer to files that have since moved. CP configuration is not tested by this operation.", "Restore and restart", "Cancel") != "Restore and restart")
                return new(CommandOutcome.Cancelled, "Restore cancelled; current state retained.");
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in new[] { "--restore-state", _operationalPaths.Root, review.Snapshot, review.Hash, Environment.ProcessId.ToString() }) start.ArgumentList.Add(argument);
            if (StateRestoreProbe.Root is not null) start.Environment["AXIOM_NATIVE_RESTORE_PROBE"] = StateRestoreProbe.Root;
            using var helper = Process.Start(start) ?? throw new IOException("Restore helper could not start.");
            started = true;
            Close();
            return new(CommandOutcome.Completed, "Restore helper is waiting for Axiom to close.");
        }
        catch (Exception error)
        {
            await ChoiceDialog.ShowAsync(this, "Restore not started", error.Message, "OK");
            return new(CommandOutcome.Failed, error.Message, error);
        }
        finally { if (!started && review is not null && File.Exists(review.Snapshot)) File.Delete(review.Snapshot); }
    }
}
