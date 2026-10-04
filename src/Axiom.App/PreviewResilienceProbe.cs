using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using AvaloniaEdit;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

internal static class PreviewResilienceProbe
{
    internal static string? Root { get; private set; }
    private static bool _restored;
    private static IReadOnlyList<RecoveryDraft> _expectedDrafts = [];

    internal static void Configure(string root, string? snapshot)
    {
        Root = Path.GetFullPath(root);
        if (Directory.Exists(Root)) throw new IOException("Choose a fresh resilience evidence root.");
        Directory.CreateDirectory(Root);
        var state = Path.Combine(Root, "state");
        Environment.SetEnvironmentVariable("AXIOM_STATE_ROOT", state);
        if (snapshot is null) return;
        Directory.CreateDirectory(Path.Combine(state, "State"));
        File.Copy(snapshot, Path.Combine(state, "State", "axiom.db"));
        var store = new SqliteStateStore(new AppDataPaths(state));
        store.InitializeAsync().GetAwaiter().GetResult();
        _expectedDrafts = store.GetRecoverableDraftsAsync().GetAwaiter().GetResult();
        _restored = true;
    }

    internal static async Task StartAsync(MainWindow window)
    {
        try
        {
            await window.StartupCompleted;
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            var preview = window.FindControl<NativeWebView>("Preview")!;
            var editor = window.FindControl<TextEditor>("Editor")!;
            string source;
            string path;
            var expectedImages = !_restored;
            if (_restored)
            {
                foreach (var draft in _expectedDrafts)
                    Require(window.TabsForTesting.Any(tab => tab.Session.DraftKey == draft.DraftKey && tab.Document.Text == draft.Text), "A cloned protected draft was not restored exactly.");
                var note = window.TabsForTesting.FirstOrDefault(tab => tab.Session.Path?.EndsWith("2026-10-04" + Path.DirectorySeparatorChar + "Notes.md", StringComparison.OrdinalIgnoreCase) == true)
                    ?? window.TabsForTesting.First(tab => tab.Session.Path is not null);
                await window.SelectTabForTestingAsync(note);
                path = note.Session.Path!; source = editor.Text;
                expectedImages = new MarkdownPreviewService().RenderDocument(source, path, workspaceRoot: Path.GetDirectoryName(Path.GetDirectoryName(path))).Contains("data:image/", StringComparison.Ordinal);
            }
            else
            {
                var workspace = Path.Combine(Root!, "workspace"); Directory.CreateDirectory(Path.Combine(workspace, "assets"));
                using var logo = Avalonia.Platform.AssetLoader.Open(new Uri("avares://Axiom.App/Assets/Axiom.png"));
                using (var image = File.Create(Path.Combine(workspace, "assets", "large.png")))
                {
                    await logo.CopyToAsync(image);
                    image.SetLength(4_302_092); // Valid PNG with harmless bytes after IEND, matching the reported asset size.
                }
                path = Path.Combine(workspace, "large.md");
                source = "# Large preview\n\n![Large image](assets/large.png)\n\n```csharp\npublic int Value = 42;\n```\n";
                await File.WriteAllTextAsync(path, source);
                await window.SwitchWorkspaceAsync(workspace); await window.OpenDocumentAsync(path);
            }
            var savedHash = Hash(await File.ReadAllBytesAsync(path));
            var workspaceRoot = window.ActiveWorkspaceRootForTesting ?? Path.GetDirectoryName(path)!;
            var html = new MarkdownPreviewService().RenderDocument(source, path, workspaceRoot: workspaceRoot);
            Require(Encoding.UTF8.GetByteCount(html) > 2 * 1024 * 1024, "The regression did not exceed the original native string limit.");
            window.ReloadPreviewForTesting(); await WaitForPreviewAsync(preview, expectedImages);
            for (var index = 0; index < 4; index++)
            {
                editor.Document.Insert(editor.Text.Length, "\nUnsaved preview edit " + index);
                await Task.Delay(400);
                window.ReloadPreviewForTesting(); await WaitForPreviewAsync(preview, expectedImages);
            }
            await window.ProtectIfNeededForTestingAsync();
            var dirty = editor.Text;
            window.PreviewNavigationForTesting = _ => throw new IOException("Injected preview adapter failure");
            window.ReloadPreviewForTesting(); await Task.Delay(300);
            Require(window.FindControl<TextBlock>("StatusText")?.Text?.Contains("Injected preview adapter failure", StringComparison.Ordinal) == true, "Preview failure did not reach persistent status.");
            Require(editor.Text == dirty && editor.Document.UndoStack.CanUndo && Hash(await File.ReadAllBytesAsync(path)) == savedHash, "Preview failure changed source, recovery or Undo.");
            window.PreviewNavigationForTesting = null; window.ReloadPreviewForTesting(); await WaitForPreviewAsync(preview, expectedImages);
            if (!_restored)
            {
                var pdf = await new NativePdfExportRenderer(Path.Combine(Root!, "export-cache")).RenderAsync(html, PdfPageSize.A4, CancellationToken.None);
                Require(pdf.Bytes.Length > 1000, "Large HTML PDF failed.");
                await File.WriteAllBytesAsync(Path.Combine(Root!, "large-preview.pdf"), pdf.Bytes);
            }
            for (var index = 0; index < 4; index++) editor.Undo();
            Require(editor.Text == source, "Repeated large Preview changes could not undo.");
            await File.WriteAllTextAsync(Path.Combine(Root!, "result.json"), JsonSerializer.Serialize(new
            {
                Version = window.BuildIdentityForTesting.DisplayText
              , HtmlBytes = Encoding.UTF8.GetByteCount(html)
              , LargeImageLoaded = expectedImages
              , RepeatedLargeNavigationAndEditing = true
              , AdapterFailureContainedAndRetryWorks = true
              , SavedSourceAndUndoPreserved = true
              , OriginalStateUntouched = true
              , ClonedUserStateRestored = _restored
              , LargeHtmlPdfChecked = !_restored
              , ExactProtectedDraftsRestored = _expectedDrafts.Count
              , HumanAcceptance = false
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error) { await File.WriteAllTextAsync(Path.Combine(Root!, "error.txt"), error.ToString()); }
        finally { window.PreviewNavigationForTesting = null; window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard"); window.Close(); }
    }

    private static async Task WaitForPreviewAsync(NativeWebView preview, bool images)
    {
        await Task.Delay(250);
        for (var attempt = 0; attempt < 70; attempt++)
        {
            try
            {
                if (await preview.InvokeScript(images ? "document.querySelector('img')?.complete === true && document.querySelector('img')?.naturalWidth > 0" : "document.querySelector('.source-block') !== null") == "true") return;
            }
            catch (Exception) { }
            await Task.Delay(100);
        }
        throw new IOException("Large native Preview did not load.");
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static void Require(bool condition, string message) { if (!condition) throw new IOException(message); }
}
