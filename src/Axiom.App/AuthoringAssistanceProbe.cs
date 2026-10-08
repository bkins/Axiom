using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using AvaloniaEdit;
using Axiom.Core;

namespace Axiom.App;

internal static class AuthoringAssistanceProbe
{
    internal static string? Root { get; private set; }
    internal static void Configure(string root)
    {
        Root = Path.GetFullPath(root);
        if (Directory.Exists(Root)) throw new IOException("Choose a fresh authoring evidence directory.");
        Directory.CreateDirectory(Root);
        Environment.SetEnvironmentVariable("AXIOM_STATE_ROOT", Path.Combine(Root, "state"));
    }

    internal static async Task StartAsync(MainWindow window)
    {
        try
        {
            await window.StartupCompleted;
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            var editor = window.FindControl<TextEditor>("Editor")!;
            editor.TextArea.Focus();
            const string source = "# Diagram\n\n```mermaid\nflowchart LR\n A[Write] --> B[Preview]\n```\n\n```mermaid\nthis is invalid\n```\n";
            window.SetEditorTextForTesting(source);
            var preview = window.FindControl<NativeWebView>("Preview")!;
            await Task.Delay(1500);
            var status = "";
            for (var attempt = 0; attempt < 100; attempt++)
            {
                status = await preview.InvokeScript("JSON.stringify(window.axiomMermaidStatus || {})");
                var completed = await preview.InvokeScript("window.axiomMermaidStatus?.completed === 2 && document.querySelectorAll('.mermaid-diagram svg').length === 1 && document.querySelectorAll('.mermaid-error').length === 1");
                if (completed == "true") break;
                await Task.Delay(100);
            }
            Require(await preview.InvokeScript("window.axiomMermaidStatus?.completed === 2 && document.querySelectorAll('.mermaid-diagram svg').length === 1") == "true", "Mermaid rendering failed: " + status);
            Require(window.EditorTextForTesting == source && editor.Options.HighlightCurrentLine, "Rendering changed source or current-line highlight is absent.");
            Require(editor.TextArea.IsFocused, "Diagram rendering moved editor focus.");
            editor.Select(source.Length, 0);
            editor.TextArea.Focus();
            editor.TextArea.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, KeyModifiers = KeyModifiers.Shift });
            Require(window.EditorTextForTesting == source + "  \n", "Shift+Enter did not insert an explicit hard break.");
            editor.Undo();
            Require(window.EditorTextForTesting == source, "Hard break did not undo in one step.");
            window.ApplyEditorPreferencesForTesting(new EditorPreferences { ShowNonPrintingCharacters = true, ShowPreviewLineNumbers = true });
            window.UpdateLayout();
            Require(editor.Options.ShowSpaces && editor.Options.ShowTabs && window.EditorTextForTesting == source, "Non-printing markers changed source or were absent.");
            window.SetEditorTextForTesting("speling\n\n`codetypooo`\n");
            await Task.Delay(1200);
            Require(window.SpellingIssuesForTesting.Count == 1 && window.SpellingIssuesForTesting[0].Word == "speling", "Offline editor spell-check did not isolate prose typo.");
            editor.Select(editor.Text.Length, 0); editor.TextArea.Focus();
            var insertion = window.ExecuteCommandAsync("editor.insertSymbol");
            await Task.Delay(150);
            var desktop = (Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)Avalonia.Application.Current!.ApplicationLifetime!;
            var picker = desktop.Windows.OfType<UnicodePickerWindow>().Single();
            picker.Search.Text = "warning";
            await Task.Delay(100);
            Require(picker.Choices.ItemCount == 1, "Symbol search did not filter aliases.");
            picker.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            await insertion;
            Require(editor.Text.EndsWith("⚠️", StringComparison.Ordinal), "Picker lost multi-codepoint Unicode.");
            editor.Undo();
            Require(editor.Text == "speling\n\n`codetypooo`\n", "Symbol insertion was not one Undo.");
            var workspace = Path.Combine(Root!, "workspace"); Directory.CreateDirectory(workspace);
            var path = Path.Combine(workspace, "document.md"); await File.WriteAllTextAsync(path, "# Saved\n");
            await window.SwitchWorkspaceAsync(workspace); await window.OpenDocumentAsync(path);
            editor.Document.Insert(editor.Document.TextLength, "First edited tab");
            var secondPath = Path.Combine(workspace, "second.md");
            await File.WriteAllTextAsync(secondPath, "# Second\n");
            await window.OpenDocumentAsync(secondPath);
            editor.Document.Insert(editor.Document.TextLength, "Second edited tab");
            editor.TextArea.Focus();
            var saveCurrentKey = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.S, KeyModifiers = KeyModifiers.Control };
            editor.TextArea.RaiseEvent(saveCurrentKey);
            for (var attempt = 0; attempt < 100 && (window.TabsForTesting.Last().Session.IsDirty || !editor.IsEnabled); attempt++) await Task.Delay(50);
            Require(editor.IsEnabled, "Active Save did not finish restoring editor availability.");
            Require(saveCurrentKey.Handled && await File.ReadAllTextAsync(path) == "# Saved\n"
                && await File.ReadAllTextAsync(secondPath) == "# Second\nSecond edited tab"
                && window.TabsForTesting.First().Session.IsDirty, "Ctrl+S did not isolate the active document.");
            editor.Document.Insert(editor.Document.TextLength, " again");
            var saveAllKey = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.S, KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift };
            editor.TextArea.RaiseEvent(saveAllKey);
            for (var attempt = 0; attempt < 100 && window.FindControl<TextBlock>("StatusText")!.Text != "Saved all 2 modified open documents."; attempt++) await Task.Delay(50);
            Require(window.FindControl<TextBlock>("StatusText")!.Text == "Saved all 2 modified open documents.", "Save All did not finish its workflow.");
            Require(saveAllKey.Handled && await File.ReadAllTextAsync(path) == "# Saved\nFirst edited tab"
                && await File.ReadAllTextAsync(secondPath) == "# Second\nSecond edited tab again", "Ctrl+Shift+S failed to save both open tabs.");
            Require(window.EditorTextForTesting == "# Second\nSecond edited tab again" && editor.CanUndo, "Save All changed the active buffer or Undo.");
            await window.OpenDocumentAsync(path);
            window.SetEditorTextForTesting("This is speling.");
            await Task.Delay(1200);
            Require(window.SpellingIssuesForTesting.Count == 1 && window.SpellingIssuesForTesting[0].Word == "speling", "First tab spelling fixture failed.");
            await window.OpenDocumentAsync(secondPath);
            Require(window.SpellingIssuesForTesting.Count == 0, "Old spelling offsets remained on the second tab.");
            await Task.Delay(700);
            Require(window.SpellingIssuesForTesting.Count == 0, "Spelling from another tab arrived asynchronously.");
            await window.OpenDocumentAsync(path);
            Require(window.SpellingIssuesForTesting.Count == 0, "Return to a tab retained stale spelling positions.");
            await Task.Delay(1200);
            Require(window.SpellingIssuesForTesting.Count == 1 && window.SpellingIssuesForTesting[0].Word == "speling", "Returning to the first tab did not recompute spelling.");
            window.SetEditorTextForTesting("# Saved\n");
            Require((await window.ExecuteCommandAsync("document.save")).Outcome == CommandOutcome.Completed, "Fixture reset save failed.");
            window.UpdateLayout();
            var sidebar = window.FindControl<Border>("WorkspaceSidebar")!;
            var tree = window.FindControl<TreeView>("FileTree")!;
            Require(!window.FindControl<TextBlock>("DuplicateWarning")!.IsVisible
                && Math.Abs(tree.Bounds.Bottom - (sidebar.Bounds.Height - sidebar.Padding.Top - sidebar.Padding.Bottom)) < 1,
                "Workspace Files retains an empty warning gap.");
            window.ClipboardImageForTesting = () =>
            {
                using var stream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://Axiom.App/Assets/Axiom.png"));
                return Task.FromResult<Avalonia.Media.Imaging.Bitmap?>(new Avalonia.Media.Imaging.Bitmap(stream));
            };
            editor.Select(editor.Text.Length, 0);
            var pasted = await window.ExecuteCommandAsync("editor.pasteImage");
            Require(pasted.Outcome == CommandOutcome.Completed && editor.Text.Contains("assets/clipboard-", StringComparison.Ordinal) && Directory.GetFiles(Path.Combine(workspace, "assets")).Length == 1 && await File.ReadAllTextAsync(path) == "# Saved\n", "Clipboard import did not preserve saved source or asset reference.");
            editor.Undo();
            Require(editor.Text == "# Saved\n" && Directory.GetFiles(Path.Combine(workspace, "assets")).Length == 1, "Image Undo did not retain asset and restore source.");
            window.SetEditorTextForTesting(source);
            var exports = new Axiom.Infrastructure.DocumentExportService(new NativePdfExportRenderer(Path.Combine(Root!, "export-cache")));
            var exportSource = source + "\n> [!note] Portable title\n> First\\\n> Second\n";
            foreach (var extension in new[] { "html", "docx", "txt", "epub", "odt", "pdf" })
            {
                var output = Path.Combine(Root!, "authoring." + extension);
                var result = await exports.WriteAsync(new(exportSource, path), output, false, [path], Path.Combine(Root!, "state"));
                Require(File.Exists(output) && result.Warnings.Any(warning => warning.Contains("Mermaid")), "Export source fallback or warning missing: " + extension);
            }
            Require(await File.ReadAllTextAsync(path) == "# Saved\n" && editor.Text == source, "Exports changed saved or dirty source.");
            window.SetEditorTextForTesting("```mermaid\nsequenceDiagram\n Alice->>Bob: Hello\n```\n\n```mermaid\n%%{init: {\"securityLevel\": \"loose\"}}%%\nflowchart LR\n A --> B\n```\n");
            await Task.Delay(1200);
            for (var attempt = 0; attempt < 80 && await preview.InvokeScript("window.axiomMermaidStatus?.completed === 2") != "true"; attempt++) await Task.Delay(100);
            Require(await preview.InvokeScript("document.querySelectorAll('.mermaid-diagram svg').length === 1 && document.querySelector('.mermaid-error').textContent.includes('configuration is disabled')") == "true", "Live refresh, sequence diagram or embedded configuration guard failed.");
            window.SetEditorTextForTesting(source);
            await window.ApplyAppearanceForTestingAsync(AppearancePreference.Dark);
            await Task.Delay(1000);
            Require(await preview.InvokeScript("document.querySelectorAll('.mermaid-diagram svg').length === 1") == "true", "Theme refresh lost Mermaid diagram.");
            window.UpdateLayout();
            using (var screenshot = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height), new Avalonia.Vector(96,96)))
            {
                screenshot.Render(window);
                screenshot.Save(Path.Combine(Root!, "authoring-dark-avalonia-surface.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
            await File.WriteAllTextAsync(Path.Combine(Root!, "result.json"), JsonSerializer.Serialize(new { Version = window.BuildIdentityForTesting.DisplayText, SaveAllTabsAndActiveUndo = true, WorkspaceBottomGapRemoved = true, MermaidValidAndInvalid = true, SourcePreserved = true, ShiftEnterAndUndo = true, CurrentLine = true, NonPrintingMarkers = true, OfflineProseSpellCheck = true, UnicodeSearchKeyboardMultiCodepointAndUndo = true, ClipboardFixtureAssetImportAndUndo = true, HumanClipboardFormatsPending = true, AllSixExportsSourceFallbackAndWarning = true, ThemeRefresh = true, Status = status }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception) { await File.WriteAllTextAsync(Path.Combine(Root!, "error.txt"), exception.ToString()); }
        finally { window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard"); window.Close(); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new IOException(message); }
}
