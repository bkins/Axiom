using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

internal static class DocumentHistoryProbe
{
    internal static string? Root { get; private set; }

    internal static void Configure(string root)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root)) throw new IOException("Choose a fresh document history evidence directory.");
        Directory.CreateDirectory(root);
        Root = root;
        Environment.SetEnvironmentVariable("AXIOM_STATE_ROOT", Path.Combine(root, "state"));
    }

    internal static async Task StartAsync(MainWindow window)
    {
        var root = Root!;
        window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
        try
        {
            await window.StartupCompleted;
            // Evidence can live inside the source checkout; Git setup must exercise an independent workspace.
            var workspace = Path.Combine(Path.GetTempPath(), "AxiomHistoryWorkspace-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workspace);
            var path = Path.Combine(workspace, "notes ü.md");
            var identity = Guid.NewGuid();
            var historical = $"<!-- axiom:documentId={identity:D} -->\n# Earlier revision\n\nCafé historical source\n";
            var current = $"<!-- axiom:documentId={identity:D} -->\n# Current revision\n\n日本語 current source\n";
            await File.WriteAllTextAsync(path, historical);
            await window.SwitchWorkspaceAsync(workspace);
            var setup = window.ExecuteCommandAsync("workspace.historySetup");
            var setupDialog = await WaitForWindowAsync<ChoiceDialog>();
            Click(setupDialog, "Create Git repository");
            Require((await setup).Outcome == CommandOutcome.Completed, "Actual Git setup command failed.");
            var service = new GitDocumentHistoryService();
            var location = (await service.LocateAsync(path))!;
            await service.CheckpointAsync(location, await service.ReviewCheckpointAsync(location));
            await File.WriteAllTextAsync(path, current);
            await service.CheckpointAsync(location, await service.ReviewCheckpointAsync(location));
            await window.OpenDocumentAsync(path);
            var state = new SqliteStateStore(new AppDataPaths(Path.Combine(root, "state")));
            var unsaved = current + "\nWorking buffer before restore";
            window.SetEditorTextForTesting(unsaved);
            await window.ProtectIfNeededForTestingAsync();
            window.UnsavedChoiceForTesting = null;
            var cancelledRestore = window.ExecuteCommandAsync("document.history");
            var cancelledHistory = await WaitForWindowAsync<DocumentHistoryWindow>();
            cancelledHistory.GetLogicalDescendants().OfType<ListBox>().Single().SelectedIndex = 1;
            await WaitAsync(() => cancelledHistory.GetLogicalDescendants().OfType<TextBox>().Single().Text == historical);
            Click(cancelledHistory, "Restore into buffer…");
            Click(await WaitForWindowAsync<ChoiceDialog>("Restore historical revision"), "Restore");
            Click(await WaitForWindowAsync<ChoiceDialog>("Unsaved changes"), "Cancel");
            Require((await cancelledRestore).Outcome == CommandOutcome.Cancelled && window.EditorTextForTesting == unsaved,
                "Unsaved restore cancellation changed the buffer.");
            Require((await state.GetRecoverableDraftsAsync()).Any(draft => draft.Text == unsaved) && await File.ReadAllTextAsync(path) == current,
                "Unsaved restore cancellation lost recovery or changed disk.");
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            var operation = window.ExecuteCommandAsync("document.history");
            var history = await WaitForWindowAsync<DocumentHistoryWindow>();
            var revisions = history.GetLogicalDescendants().OfType<ListBox>().Single();
            revisions.SelectedIndex = 1;
            var historicalSource = history.GetLogicalDescendants().OfType<TextBox>().Single();
            await WaitAsync(() => historicalSource.Text == historical);
            Click(history, "Compare");
            var difference = await WaitForWindowAsync<LineDiffWindow>();
            var comparison = difference.GetLogicalDescendants().OfType<TextBox>().Single();
            Require(comparison.IsReadOnly && comparison.Text!.Contains("Earlier revision") && comparison.Text.Contains("Current revision"), "Native line comparison did not contain both revisions.");
            Click(difference, "Close");
            Click(history, "Restore into buffer…");
            var restore = await WaitForWindowAsync<ChoiceDialog>();
            Click(restore, "Restore");
            Require((await operation).Outcome == CommandOutcome.Completed, "Actual restore command failed.");
            Require(window.EditorTextForTesting == historical && window.TabsForTesting.Last().Session.IsDirty, "Restored buffer was not dirty historical source.");
            Require(await File.ReadAllTextAsync(path) == current, "Restore changed saved source.");
            Require((await state.GetRecoverableDraftsAsync()).Any(draft => draft.Text == historical), "Restored native buffer was not protected.");
            await window.ExecuteCommandAsync("editor.undo");
            Require(window.EditorTextForTesting == current, "Native restore Undo failed.");
            await window.ExecuteCommandAsync("editor.redo");
            Require(window.EditorTextForTesting == historical, "Native restore Redo failed.");
            Require((await window.ExecuteCommandAsync("document.save")).Outcome == CommandOutcome.Completed, "Explicit restored Save failed.");
            Require(await File.ReadAllTextAsync(path) == historical, "Explicit Save did not save restored source.");
            Require((await service.ListAsync(location)).Count == 2, "Save created an automatic Git checkpoint.");
            var cancelledCheckpoint = window.ExecuteCommandAsync("document.checkpoint");
            Click(await WaitForWindowAsync<ChoiceDialog>("Checkpoint saved document"), "Cancel");
            Require((await cancelledCheckpoint).Outcome == CommandOutcome.Cancelled && (await service.ListAsync(location)).Count == 2,
                "Cancelled checkpoint created a revision.");
            var checkpoint = window.ExecuteCommandAsync("document.checkpoint");
            var checkpointDialog = await WaitForWindowAsync<ChoiceDialog>();
            Click(checkpointDialog, "Checkpoint");
            Require((await checkpoint).Outcome == CommandOutcome.Completed && (await service.ListAsync(location)).Count == 3, "Deliberate native checkpoint failed.");
            await File.WriteAllTextAsync(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new
            {
                Version                           = window.BuildIdentityForTesting.InformationalVersion
              , Workspace                         = workspace
              , ActualSetupAndCheckpointDialogs   = true
              , NativeRevisionPreviewAndLineDiff  = true
              , RestoreProtectedBufferDiskUnchanged = true
              , RestoreUndoRedoAndExplicitSave    = true
              , SaveDoesNotAutomaticallyCheckpoint = true
              , UnsavedRestoreAndCheckpointCancel = true
              , IdentityPreserved                 = DocumentIdCodec.Find(window.EditorTextForTesting) == identity
              , Exclusions                        = "Physical keyboard/focus/theme/DPI and sustained very large history latency remain pending human checks. Git is installed separately."
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception) { await File.WriteAllTextAsync(Path.Combine(root, "error.txt"), exception.ToString()); }
        finally
        {
            foreach (var open in Windows().Where(open => open != window).ToArray()) open.Close();
            foreach (var tab in window.TabsForTesting.ToArray())
            {
                await window.SelectTabForTestingAsync(tab);
                window.SetEditorTextForTesting(tab.Session.SavedText);
            }
            window.Close();
        }
    }

    private static IEnumerable<Window> Windows() => ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Windows;

    private static void Click(Window window, string title) => window.GetLogicalDescendants().OfType<Button>()
        .Single(button => Equals(button.Content, title)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task<TWindow> WaitForWindowAsync<TWindow>(string? title = null) where TWindow : Window
    {
        await WaitAsync(() => Windows().OfType<TWindow>().Any(window => title is null || window.Title == title));
        return Windows().OfType<TWindow>().Single(window => title is null || window.Title == title);
    }

    private static async Task WaitAsync(Func<bool> check)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (check()) return;
            await Task.Delay(100);
        }
        throw new IOException("Native document history check timed out.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new IOException(message);
    }
}
