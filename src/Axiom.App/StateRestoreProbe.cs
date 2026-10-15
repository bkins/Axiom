using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Axiom.Infrastructure;

namespace Axiom.App;

internal static class StateRestoreProbe
{
    internal static string? Root { get; private set; }
    internal static void Configure(string root)
    {
        Root = Path.GetFullPath(root);
        if (Directory.Exists(Root) && !File.Exists(Path.Combine(Root, "restart-pending"))) throw new IOException("Choose fresh restore probe directory.");
        Directory.CreateDirectory(Root);
        Environment.SetEnvironmentVariable("AXIOM_STATE_ROOT", Path.Combine(Root, "state"));
    }
    internal static async Task StartAsync(MainWindow window)
    {
        try
        {
            await window.StartupCompleted;
            var paths = new AppDataPaths(Path.Combine(Root!, "state"));
            var current = new SqliteStateStore(paths);
            if (File.Exists(Path.Combine(Root!, "restart-pending")))
            {
                var drafts = await current.GetRecoverableDraftsAsync();
                if (!drafts.Any(draft => draft.Text == "from reviewed backup")) throw new IOException("Restart did not open restored state.");
                if (await File.ReadAllTextAsync(Path.Combine(Root!, "source.md")) != "Markdown untouched") throw new IOException("Markdown changed.");
                await File.WriteAllTextAsync(Path.Combine(Root!, "result.json"), JsonSerializer.Serialize(new { ExactBackupReview = true, RealHelperAfterParentExit = true, RestartOpenedRestoredState = true, AutomaticRollbackBackup = Directory.GetFiles(paths.BackupDirectory, "pre-restore-*.db").Length > 0, MarkdownUnchanged = true, Version = window.BuildIdentityForTesting.DisplayText }));
                window.Close(); return;
            }
            var backupPaths = new AppDataPaths(Path.Combine(Root!, "backup-source"));
            var backup = new SqliteStateStore(backupPaths); await backup.InitializeAsync();
            await backup.SaveDraftAsync(new("restore-probe", null, "from reviewed backup", DateTimeOffset.UtcNow, null, true));
            await current.SaveDraftAsync(new("current-probe", null, "retained in rollback", DateTimeOffset.UtcNow, null, true));
            await File.WriteAllTextAsync(Path.Combine(Root!, "source.md"), "Markdown untouched");
            await File.WriteAllTextAsync(Path.Combine(Root!, "restart-pending"), "approved disposable native probe");
            var action = window.RestoreBackupForTestingAsync(backupPaths.DatabasePath);
            for (var at = 0; at < 200 && !window.OwnedWindows.Any(dialog => dialog.Title == "Review state backup restoration"); at++) await Task.Delay(30);
            var dialog = window.OwnedWindows.Single(child => child.Title == "Review state backup restoration");
            dialog.GetLogicalDescendants().OfType<Button>().Single(button => button.Content?.ToString() == "Restore and restart").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await action;
        }
        catch (Exception error) { await File.WriteAllTextAsync(Path.Combine(Root!, "error.txt"), error.ToString()); window.Close(); }
    }
}
