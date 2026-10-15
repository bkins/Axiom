using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

internal static class CreationProbe
{
    internal static string? Root { get; private set; }
    internal static void Configure(string root)
    {
        Root = Path.GetFullPath(root);
        if (Directory.Exists(Root)) throw new IOException("Choose fresh creation evidence.");
        Directory.CreateDirectory(Root);
        Environment.SetEnvironmentVariable("AXIOM_STATE_ROOT", Path.Combine(Root, "state"));
    }

    internal static async Task StartAsync(MainWindow main)
    {
        try
        {
            await main.StartupCompleted;
            main.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            await main.ToggleReadingModeAsync();
            await main.ExecuteCommandAsync("document.new");
            Require(main.ActiveWorkspaceRootForTesting is null && !main.ReadingModeForTesting, "Standalone New did not restore editor without workspace.");
            const string text = "# New standalone document\n\nSaved outside a workspace.\n";
            main.FindControl<AvaloniaEdit.TextEditor>("Editor")!.Document.Text = text;
            await File.WriteAllTextAsync(Path.Combine(Root!, "save-dialog-ready.txt"), Path.Combine(Root!, "standalone.md"));
            // Exercise the real OS Save As dialog; desktop automation supplies only a fresh fixture path.
            await main.ExecuteCommandAsync("document.saveAs");
            var savedPath = Path.Combine(Root!, "standalone.md");
            Require(File.Exists(savedPath) && main.ActiveWorkspaceRootForTesting is null && main.PathContextForTesting == savedPath, "Standalone Save As did not finish outside a workspace.");
            Require(await File.ReadAllTextAsync(savedPath) == text, "Standalone saved bytes differ.");

            var creation = main.ExecuteCommandAsync("workspace.create");
            await Wait(() => main.OwnedWindows.OfType<NewWorkspaceWindow>().Any());
            var dialog = main.OwnedWindows.OfType<NewWorkspaceWindow>().Single();
            Control<TextBox>(dialog, "WorkspaceLocation").Text = Root;
            Control<TextBox>(dialog, "WorkspaceName").Text = "My new workspace";
            Click(dialog, "WorkspaceCreate");
            var result = await creation;
            var workspace = Path.Combine(Root!, "My new workspace");
            Require(result.Outcome == CommandOutcome.Completed && main.ActiveWorkspaceRootForTesting == workspace && Directory.Exists(workspace), "Create Workspace form did not create and activate the folder.");
            var state = new SqliteStateStore(new AppDataPaths(Path.Combine(Root!, "state")));
            Require((await state.ListWorkspacesAsync()).Any(record => record.RootPath == workspace), "New workspace was not remembered.");
            Require(await File.ReadAllTextAsync(savedPath) == text, "Workspace creation altered the standalone file.");
            await main.ExecuteCommandAsync("document.new");
            Require(!main.ReadingModeForTesting && main.TabsForTesting.Last().Session.Path is null, "New document in workspace failed.");
            creation = main.ExecuteCommandAsync("workspace.create");
            await Wait(() => main.OwnedWindows.OfType<NewWorkspaceWindow>().Any());
            dialog = main.OwnedWindows.OfType<NewWorkspaceWindow>().Single();
            Click(dialog, "WorkspaceCancel");
            Require((await creation).Outcome == CommandOutcome.Cancelled && main.ActiveWorkspaceRootForTesting == workspace, "Cancelled dialog changed workspace.");
            await File.WriteAllTextAsync(Path.Combine(Root!, "result.json"), JsonSerializer.Serialize(new
            {
                Version = main.BuildIdentityForTesting.InformationalVersion,
                StandaloneNewCommand = true, NewShowsEditorFromReadingMode = true,
                ActualNativeSaveAsOutsideWorkspace = true, SavedBytesUnchanged = true,
                ActualCreateWorkspaceDialog = true, NewFolderActivated = true, WorkspaceRemembered = true,
                CancelLeavesWorkspaceUnchanged = true, NewDocumentInsideWorkspace = true,
                SyntheticFilesOnly = true, HumanAcceptance = false
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error) { await File.WriteAllTextAsync(Path.Combine(Root!, "error.txt"), error.ToString()); }
        finally { foreach (var window in main.OwnedWindows.ToArray()) window.Close(); main.Close(); }
    }

    private static T Control<T>(Window window, string name) where T : Control => window.GetLogicalDescendants().OfType<T>().Single(control => control.Name == name);
    private static void Click(Window window, string name) => Control<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Require(bool value, string message) { if (!value) throw new IOException(message); }
    private static async Task Wait(Func<bool> ready)
    {
        var end = DateTime.UtcNow.AddSeconds(15);
        while (!ready()) { if (DateTime.UtcNow > end) throw new TimeoutException("Creation UI did not appear."); await Task.Delay(50); }
    }
}
