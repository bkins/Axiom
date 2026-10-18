using System.Diagnostics;
using System.Text.Json;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

internal static class NextUiStoriesProbe
{
    internal static string? Root { get; private set; }
    private static bool _registeredOpening;

    internal static void Configure(string root, bool registeredOpening = false)
    {
        Root = Path.GetFullPath(root);
        if (Directory.Exists(Root)) throw new IOException("Choose fresh UI-story evidence.");
        Directory.CreateDirectory(Root);
        _registeredOpening = registeredOpening;
        if (registeredOpening)
        {
            var record = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Axiom", "ReviewOpenWith", "registration.json");
            using var json = JsonDocument.Parse(File.ReadAllText(record));
            var expected = ApplicationBuildIdentity.FromAssembly(typeof(Program).Assembly).InformationalVersion;
            if (json.RootElement.GetProperty("Candidate").GetString() != expected) throw new IOException("Registered build does not match this probe.");
            var state = json.RootElement.GetProperty("StateRoot").GetString()!;
            if (Directory.Exists(state)) throw new IOException("Registered opening probe requires an unused candidate review profile.");
            Environment.SetEnvironmentVariable("AXIOM_STATE_ROOT", state);
        }
        else Environment.SetEnvironmentVariable("AXIOM_STATE_ROOT", Path.Combine(Root, "state"));
    }

    internal static async Task StartAsync(MainWindow window)
    {
        try
        {
            await window.StartupCompleted;
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            if (_registeredOpening) { await CheckRegisteredOpening(window); return; }
            var root = Path.Combine(Root!, "workspace");
            Directory.CreateDirectory(Path.Combine(root, "source", "empty"));
            Directory.CreateDirectory(Path.Combine(root, "target"));
            var id = Guid.NewGuid();
            var original = DocumentIdCodec.Enroll("# Saved\n[Shared](../shared.md)\n![Asset](asset.bin)", id);
            var source = Path.Combine(root, "source", "note.md");
            await File.WriteAllTextAsync(source, original);
            await File.WriteAllBytesAsync(Path.Combine(root, "source", "asset.bin"), [1, 2, 3]);
            await File.WriteAllTextAsync(Path.Combine(root, "shared.md"), "# Shared");
            const string tasks = "# Tasks\n- [ ] One\n- [x] Two\n";
            var taskPath = Path.Combine(root, "tasks.md");
            await File.WriteAllTextAsync(taskPath, tasks);
            await window.SwitchWorkspaceAsync(root);
            await window.OpenDocumentAsync(source);
            window.SetEditorTextForTesting(original + "\nUnsaved original");
            window.Topmost = true; window.Activate();
            Require((await window.ExecuteExplorerCommandForTestingAsync("workspace.copyFile", "source", true)).Outcome == CommandOutcome.Completed, "Folder Copy failed.");
            var paste = window.ExecuteExplorerCommandForTestingAsync("workspace.pasteFile", "target", true);
            await Answer(window, "Paste", "Continue", "target/copy");
            await Answer(window, "Paste folder", "Paste");
            Require((await paste).Outcome == CommandOutcome.Completed, "Folder Paste failed.");
            var copied = await File.ReadAllTextAsync(Path.Combine(root, "target", "copy", "note.md"));
            Require(DocumentIdCodec.Find(copied) is { } copyId && copyId != id && !copied.Contains("Unsaved original"), "Folder Paste did not preserve saved snapshot/fresh identity.");
            Require(copied.Contains("[Shared](../../shared.md)") && Directory.Exists(Path.Combine(root, "target", "copy", "empty")), "Copied links or empty folder missing.");
            Require((await File.ReadAllBytesAsync(Path.Combine(root, "target", "copy", "asset.bin"))).SequenceEqual(new byte[] { 1, 2, 3 }), "Copied asset changed.");
            Require(window.EditorTextForTesting.EndsWith("Unsaved original", StringComparison.Ordinal) && await File.ReadAllTextAsync(source) == original, "Folder Paste changed original buffer/disk.");
            Require((await window.ExecuteExplorerCommandForTestingAsync("workspace.copyFile", "source/note.md")).Outcome == CommandOutcome.Completed, "File Copy failed.");
            paste = window.ExecuteExplorerCommandForTestingAsync("workspace.pasteFile", "target", true);
            await Answer(window, "Paste", "Continue", "target/file-copy.md");
            await Answer(window, "Paste file", "Paste");
            Require((await paste).Outcome == CommandOutcome.Completed, "File Paste failed.");
            Require(DocumentIdCodec.Find(await File.ReadAllTextAsync(Path.Combine(root, "target", "file-copy.md"))) != id, "File Paste reused identity.");

            await window.OpenDocumentAsync(taskPath);
            var preview = window.FindControl<NativeWebView>("Preview")!;
            await Wait(async () => (await preview.InvokeScript("document.querySelectorAll('a.task-toggle').length"))?.Trim('"') == "2");
            var bounds = await preview.InvokeScript("(() => { const box = document.querySelector('a.task-toggle').getBoundingClientRect(); return { x:box.x+box.width/2,y:box.y+box.height/2 }; })()");
            using (var json = JsonDocument.Parse(bounds!))
                await NativeProbeInput.ClickOwnedWebViewAsync(preview, new Avalonia.Point(json.RootElement.GetProperty("x").GetDouble(), json.RootElement.GetProperty("y").GetDouble()));
            await Wait(() => Task.FromResult(window.EditorTextForTesting.Contains("- [x] One")));
            await window.ProtectIfNeededForTestingAsync();
            var state = new SqliteStateStore(new AppDataPaths(Path.Combine(Root!, "state")));
            Require((await state.GetRecoverableDraftsAsync()).Any(draft => draft.Text.Contains("- [x] One")), "Task edit did not enter recovery.");
            Require(await File.ReadAllTextAsync(taskPath) == tasks, "Preview click saved Markdown.");
            await window.ExecuteCommandAsync("editor.undo");
            Require(window.EditorTextForTesting == tasks, "Preview toggle was not one Undo.");
            await Wait(async () => (await preview.InvokeScript("document.querySelector('a.task-toggle')?.getAttribute('aria-checked')"))?.Trim('"') == "false");
            var headingBounds = await preview.InvokeScript("(() => { const box = document.querySelector('h1').getBoundingClientRect(); return { x:box.x+box.width/2,y:box.y+box.height/2 }; })()");
            using (var json = JsonDocument.Parse(headingBounds!))
                await NativeProbeInput.ClickOwnedWebViewAsync(preview, new Avalonia.Point(json.RootElement.GetProperty("x").GetDouble(), json.RootElement.GetProperty("y").GetDouble()));
            await preview.InvokeScript("document.querySelector('a.task-toggle').focus(); true");
            await Task.Delay(120);
            NativeProbeInput.Key(0x20);
            await Wait(() => Task.FromResult(window.EditorTextForTesting.Contains("- [x] One")));
            Require(await File.ReadAllTextAsync(taskPath) == tasks, "Keyboard toggle saved Markdown.");
            await Wait(async () => (await preview.InvokeScript("document.activeElement?.matches('a.task-toggle') && document.activeElement.getAttribute('aria-checked') === 'true'"))?.Trim('"') == "true");
            NativeProbeInput.Key(0x20);
            await Wait(() => Task.FromResult(window.EditorTextForTesting == tasks));
            await File.WriteAllTextAsync(Path.Combine(Root!, "result.json"), JsonSerializer.Serialize(new
            {
                Version = window.BuildIdentityForTesting.InformationalVersion,
                RegisteredFolderCopyPaste = true, ActualPasteDialogs = true, SavedSnapshotAndFreshIdentity = true,
                AssetsEmptyFoldersAndReferences = true, RegisteredFileCopyPaste = true,
                NativeWebViewTaskClick = true, PhysicalCheckboxPointerClick = true, NativeSpaceKeyTaskToggle = true, CheckboxKeyboardFocusRetained = true, SingleUndo = true,
                ProtectedDraftIncludesTaskEdit = true, SourceAndUnsavedBufferRetained = true, HumanAcceptance = false
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error)
        {
            await File.WriteAllTextAsync(Path.Combine(Root!, "error.txt"), error.ToString());
            try
            {
                var preview = window.FindControl<NativeWebView>("Preview")!;
                var dom = await preview.InvokeScript("JSON.stringify({ active:document.activeElement?.outerHTML, keys:window.__axiomViewKeys, token:document.documentElement.dataset.linkToken })");
                await File.WriteAllTextAsync(Path.Combine(Root!, "diagnostic.json"), JsonSerializer.Serialize(new { Text=window.EditorTextForTesting, Dom=dom }));
            }
            catch (Exception) { }
        }
        finally { window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard"); window.Close(); }
    }

    private static async Task CheckRegisteredOpening(MainWindow window)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var path = Path.Combine(Root!, "Opening check [literal] & sample.md");
        const string text = "# Disposable Windows opening check\n";
        await File.WriteAllTextAsync(path, text);
        using var choice = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.md\UserChoice");
        var program = choice?.GetValue("ProgId") as string;
        var defaultIsAxiom = program is "Axiom.Review.Markdown" or @"Applications\Axiom.App.exe";
        void Open()
        {
            if (defaultIsAxiom) { using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, ErrorDialog = false }); }
            else
            {
                var shell = new ShellOpen { Size=Marshal.SizeOf<ShellOpen>(), Mask=0x441, Verb="open", File=path, Class="Axiom.Review.Markdown", Show=1 };
                if (!ShellExecuteEx(ref shell)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                if (shell.Process != 0) CloseHandle(shell.Process);
            }
        }
        Open();
        await Wait(() => Task.FromResult(window.TabsForTesting.Any(tab => tab.Session.Path == path)), 600);
        Require(window.EditorTextForTesting == text, "Shell-open document was not received.");
        window.SetEditorTextForTesting(text + "Unsaved opening check");
        Open();
        await Task.Delay(2000);
        Require(window.TabsForTesting.Count(tab => tab.Session.Path == path) == 1 && window.EditorTextForTesting.EndsWith("Unsaved opening check", StringComparison.Ordinal), "Shell repeat changed the dirty buffer.");
        var dispatch = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Axiom", "ReviewOpenWith", "last-open.json");
        using var record = JsonDocument.Parse(await File.ReadAllTextAsync(dispatch));
        Require(record.RootElement.GetProperty("Candidate").GetString() == window.BuildIdentityForTesting.InformationalVersion, "Shell dispatch logged another version.");
        Require(await File.ReadAllTextAsync(path) == text, "Shell opening saved source.");
        await File.WriteAllTextAsync(Path.Combine(Root!, "result.json"), JsonSerializer.Serialize(new
        {
            Version = window.BuildIdentityForTesting.InformationalVersion, ExplicitWindowsAxiomAssociation = true,
            RealRegisteredWrapper = true, ExactRunningVersion = true, LiteralSpacedDocumentArgument = true,
            RepeatRetainsDirtyBufferAndSingleTab = true, SavedSourceRetained = true,
            DefaultDoubleClickVerified = defaultIsAxiom, SavedDefaultProgId = program,
            UsedFreshCandidateReviewState = true, DefaultChoiceModified = false, HumanAcceptance = false
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    private struct ShellOpen
    {
        public int Size; public uint Mask; public nint Window;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Verb;
        [MarshalAs(UnmanagedType.LPWStr)] public string? File;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Parameters;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Directory;
        public int Show; public nint Instance; public nint IdList;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Class;
        public nint ClassKey; public uint HotKey; public nint Icon; public nint Process;
    }
    [DllImport("shell32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern bool ShellExecuteEx(ref ShellOpen info);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint handle);

    private static async Task Wait(Func<Task<bool>> condition, int attempts = 300)
    {
        for (var at = 0; at < attempts; at++)
        {
            try { if (await condition()) return; } catch (Exception) { }
            await Task.Delay(40);
        }
        throw new TimeoutException("Native UI-story condition timed out.");
    }

    private static async Task Answer(Window owner, string title, string choice, string? text = null)
    {
        for (var at = 0; at < 300; at++)
        {
            if (owner.OwnedWindows.SingleOrDefault(dialog => dialog.Title == title) is { } dialog)
            {
                if (text is not null) dialog.GetLogicalDescendants().OfType<TextBox>().Single().Text = text;
                dialog.Topmost = true; dialog.Activate(); await Task.Delay(160); dialog.UpdateLayout();
                await NativeProbeInput.ClickAsync(dialog.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, choice)));
                return;
            }
            await Task.Delay(30);
        }
        throw new TimeoutException("Missing native dialog: " + title);
    }
    private static void Require(bool success, string message) { if (!success) throw new IOException(message); }
}
