using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

internal static class DeletedDocumentProbe
{
    internal static string? Root { get; private set; }

    internal static void Configure(string root)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root)) throw new IOException("Choose a fresh recovery evidence directory.");
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
            var workspaceRoot = Path.Combine(root, "workspace");
            var parent = Path.Combine(workspaceRoot, "notes");
            Directory.CreateDirectory(parent);
            var path = Path.Combine(parent, "deleted.md");
            var identity = Guid.NewGuid();
            var text = $"<!-- axiom:documentId={identity:D} -->\r\n# Native recovery\r\n\r\nCafé 日本語\r\n";
            await File.WriteAllTextAsync(path, text);
            var bytes = await File.ReadAllBytesAsync(path);
            await window.SwitchWorkspaceAsync(workspaceRoot);
            await window.OpenDocumentAsync(path);
            var state = new SqliteStateStore(new AppDataPaths(Path.Combine(root, "state")));
            var workspace = (await state.ListWorkspacesAsync()).Single(item => item.RootPath == workspaceRoot);
            await state.AddPinAsync(workspace.WorkspaceId, "notes" + Path.DirectorySeparatorChar + "deleted.md", DateTimeOffset.UtcNow);
            var fileCommands = window.FindControl<Button>("FileCommandsButton")!;
            var recycle = ((MenuFlyout)fileCommands.Flyout!).Items.OfType<MenuItem>().First(item => Equals(item.Tag, "Recycle"));
            recycle.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Click(await WaitForWindowAsync<ChoiceDialog>(), "Continue");
            await WaitAsync(() => !File.Exists(path));
            await WaitAsync(() => !window.TabsForTesting.Any(tab => tab.Session.Path == path));
            await WaitAsync(() => window.FindControl<WrapPanel>("CommandPanel")!.IsEnabled);
            var item = AssertSingle(await state.GetDeletedDocumentsAsync(workspace.WorkspaceId));
            Require(item.Status == "Available" && bytes.SequenceEqual(item.Content), "Actual recycle did not capture exact bytes.");
            Directory.Delete(parent);
            var cancel = window.ExecuteCommandAsync("workspace.deletedDocuments");
            var browser = await WaitForWindowAsync<DeletedDocumentsWindow>();
            browser.GetLogicalDescendants().OfType<ListBox>().Single().SelectedIndex = 0;
            Require(browser.GetLogicalDescendants().OfType<TextBox>().Single().Text == text, "Native recovery preview differs from captured source.");
            Click(browser, "Restore selected…");
            Click(await WaitForWindowAsync<TextEntryDialog>(), "Cancel");
            Require((await cancel).Outcome == CommandOutcome.Cancelled && !File.Exists(path), "Cancelled restore changed disk.");
            var restore = window.ExecuteCommandAsync("workspace.deletedDocuments");
            browser = await WaitForWindowAsync<DeletedDocumentsWindow>();
            browser.GetLogicalDescendants().OfType<ListBox>().Single().SelectedIndex = 0;
            Click(browser, "Restore selected…");
            Click(await WaitForWindowAsync<TextEntryDialog>(), "Continue");
            Click(await WaitForWindowAsync<ChoiceDialog>(), "Preserve identity");
            Require((await restore).Outcome == CommandOutcome.Completed, "Native restore command failed.");
            var restoredBytes = await File.ReadAllBytesAsync(path);
            Require(bytes.SequenceEqual(restoredBytes), "Native restore changed bytes.");
            Require((await state.GetPinsAsync(workspace.WorkspaceId)).Single().RelativePath == Path.GetRelativePath(workspaceRoot, path), "Native restore lost the pin.");
            await File.WriteAllTextAsync(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new
            {
                Version = window.BuildIdentityForTesting.InformationalVersion
              , ActualRecycleAndRecoveryDialogs = true
              , ExactBytesAndIdentity = true
              , RecordedParentsReconstructed = true
              , RestoreCancellationRetainsCapture = true
              , PinRestored = true
              , HumanChecks = "Physical keyboard, accessibility, DPI, themes and release acceptance remain pending."
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception) { await File.WriteAllTextAsync(Path.Combine(root, "error.txt"), exception.ToString()); }
        finally
        {
            foreach (var open in Windows().Where(open => open != window).ToArray()) open.Close();
            window.Close();
        }
    }

    private static DeletedDocument AssertSingle(IReadOnlyList<DeletedDocument> items) => items.Single();
    private static IEnumerable<Window> Windows() => ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Windows;
    private static void Click(Window window, string label) => window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task<TWindow> WaitForWindowAsync<TWindow>() where TWindow : Window
    {
        await WaitAsync(() => Windows().OfType<TWindow>().Any());
        return Windows().OfType<TWindow>().Single();
    }
    private static async Task WaitAsync(Func<bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (predicate()) return;
            await Task.Delay(100);
        }
        throw new IOException("Native deleted-document recovery check timed out.");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new IOException(message);
    }
}
