using System.Text.Json;
using Avalonia.Controls;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

internal static class ReadingFeedbackProbe
{
    internal static string? Root { get; private set; }

    internal static void Configure(string root)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root)) throw new IOException("Choose a fresh reading evidence directory.");
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
            var workspace = Path.Combine(root, "workspace");
            Directory.CreateDirectory(workspace);
            var sourcePath = Path.Combine(workspace, "long.md");
            var source = string.Join("\n\n", Enumerable.Range(1, 90).Select(index => $"## Section {index}\n\nParagraph {index}: Café 日本語 words for reading. {new string('x', 180)}"));
            await File.WriteAllTextAsync(sourcePath, source);
            await window.SwitchWorkspaceAsync(workspace);
            await window.OpenDocumentAsync(sourcePath);
            var preview = window.FindControl<NativeWebView>("Preview")!;
            var editor = window.FindControl<AvaloniaEdit.TextEditor>("Editor")!;
            await WaitAsync(async () => IsTrue(await preview.InvokeScript("document.querySelectorAll('.source-block').length === 180")));
            window.ToggleScrollSyncForTesting();
            await window.SynchronizeScrollForTestingAsync();
            await Task.Delay(250);
            var offset = source.IndexOf("## Section 45\n", StringComparison.Ordinal);
            editor.CaretOffset = offset;
            await window.ScrollPreviewToSourceAsync(offset);
            await WaitAsync(async () => IsTrue(await preview.InvokeScript("Math.abs(document.querySelectorAll('.source-block')[88].getBoundingClientRect().top - 12) < 25")));
            // Capture/restore the block through an edit preceding the viewport.
            window.SetEditorTextForTesting("Intro inserted before existing blocks.\n\n" + source);
            await WaitAsync(async () => IsTrue(await preview.InvokeScript("document.querySelectorAll('.source-block').length === 181")));
            await WaitAsync(async () => IsTrue(await preview.InvokeScript("Math.abs(document.querySelectorAll('.source-block')[89].getBoundingClientRect().top - 12) < 35")));
            var dirty = window.EditorTextForTesting;
            // Native Source navigation exercises the app-owned URI/token path.
            await preview.InvokeScript("document.querySelectorAll('.source-navigation')[50].click()");
            var expectedCaret = int.Parse((await preview.InvokeScript("document.querySelectorAll('.source-block')[50].dataset.sourceStart"))!.Trim('"'), System.Globalization.CultureInfo.InvariantCulture);
            await WaitAsync(() => Task.FromResult(window.EditorCaretOffsetForTesting == expectedCaret));
            // Editor viewport changes feed the native browser by structural offset.
            await Task.Delay(600);
            var line = editor.Document.GetLineByOffset(dirty.IndexOf("## Section 70\n", StringComparison.Ordinal)).LineNumber;
            editor.ScrollToLine(line);
            await WaitAsync(async () => IsTrue(await preview.InvokeScript("window.scrollY > document.querySelectorAll('.source-block')[100].offsetTop")));
            // Browser input + scroll signals feed the editor without moving its caret.
            await window.SynchronizeScrollForTestingAsync();
            await preview.InvokeScript("window.dispatchEvent(new WheelEvent('wheel', {deltaY: 600})); window.scrollTo(0, document.querySelectorAll('.source-block')[24].offsetTop)");
            await Task.Delay(250);
            await window.SynchronizeScrollForTestingAsync();
            await WaitAsync(() => Task.FromResult(editor.TextArea.TextView.VerticalOffset < 1200));
            if (window.EditorCaretOffsetForTesting != expectedCaret) throw new IOException("Scroll sync moved the source caret.");
            var editorBefore = editor.TextArea.TextView.VerticalOffset;
            var previewBefore = double.Parse((await preview.InvokeScript("window.scrollY"))!, System.Globalization.CultureInfo.InvariantCulture);
            await Task.Delay(1200);
            if (Math.Abs(editor.TextArea.TextView.VerticalOffset - editorBefore) > 5 ||
                Math.Abs(double.Parse((await preview.InvokeScript("window.scrollY"))!, System.Globalization.CultureInfo.InvariantCulture) - previewBefore) > 5)
                throw new IOException("Scroll synchronization produced a feedback loop.");
            window.ToggleScrollSyncForTesting();
            var retainedScroll = double.Parse((await preview.InvokeScript("window.scrollY"))!, System.Globalization.CultureInfo.InvariantCulture);
            var otherPath = Path.Combine(workspace, "other.md");
            await File.WriteAllTextAsync(otherPath, "# Other tab");
            await window.OpenDocumentAsync(otherPath);
            await WaitAsync(async () => IsTrue(await preview.InvokeScript("document.querySelectorAll('.source-block').length === 1")));
            await window.OpenDocumentAsync(sourcePath);
            await WaitAsync(async () => IsTrue(await preview.InvokeScript("document.querySelectorAll('.source-block').length === 181")));
            await WaitAsync(async () => Math.Abs(double.Parse((await preview.InvokeScript("window.scrollY"))!, System.Globalization.CultureInfo.InvariantCulture) - retainedScroll) < 35);
            await window.ToggleReadingModeAsync();
            await preview.InvokeScript("window.scrollTo(0, 0)");
            await window.ToggleReadingModeAsync();
            if (window.EditorTextForTesting != dirty || await File.ReadAllTextAsync(sourcePath) != source)
                throw new IOException("Reading/navigation changed source or buffer text.");
            window.RefreshStatisticsForTesting();
            if (!window.FindControl<TextBlock>("DocumentFormatText")!.Text!.Contains("LF", StringComparison.Ordinal))
                throw new IOException("Newline status missing.");
            window.ShowFailureForTesting("Fixture: output unavailable — retry after selecting an available destination.");
            window.ShowInformationForTesting("Reading action succeeded");
            await Task.Delay(8200);
            if (!window.FindControl<TextBlock>("StatusText")!.Text!.StartsWith("Fixture:", StringComparison.Ordinal))
                throw new IOException("Persistent failure was overwritten or expired.");
            window.DismissFailureForTesting();
            if (window.FindControl<Button>("DismissStatusButton")!.IsVisible) throw new IOException("Failure dismissal did not clear the control.");
            await Task.Delay(8200);
            if (window.FindControl<TextBlock>("StatusText")!.Text != "Ready") throw new IOException("Informational feedback did not expire.");
            await window.ProtectIfNeededForTestingAsync();
            await File.WriteAllTextAsync(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new
            {
                Version = window.BuildIdentityForTesting.InformationalVersion
              , NativeAnchorsAndSourceNavigation = true
              , EditorToPreviewAndPreviewToEditorScroll = true
              , EditAnchorRetention = true
              , TabPreviewPositionRetained = true
              , NoObservedScrollFeedbackLoop = true
              , ReadingModeAndSourceBytesPreserved = true
              , StatisticsAndPersistentFailureDismissal = true
              , InformationExpires = true
              , Exclusions = "Physical wheel/focus/theme/DPI combinations and very large document latency require later human/native coverage."
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception) { await File.WriteAllTextAsync(Path.Combine(root, "error.txt"), exception.ToString()); }
        finally
        {
            foreach (var tab in window.TabsForTesting.ToArray())
            {
                await window.SelectTabForTestingAsync(tab);
                window.SetEditorTextForTesting(tab.Session.SavedText);
            }
            window.Close();
        }
    }

    private static bool IsTrue(string? value) => value?.Trim().Trim('"') == "true";

    private static async Task WaitAsync(Func<Task<bool>> check)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await check()) return;
            await Task.Delay(100);
        }
        throw new IOException("Native reading/scroll check timed out.");
    }
}
