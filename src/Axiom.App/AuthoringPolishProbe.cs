using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using AvaloniaEdit;
using Axiom.Core;

namespace Axiom.App;

internal static class AuthoringPolishProbe
{
    internal static string? Root { get; private set; }
    internal static void Configure(string root)
    {
        Root = Path.GetFullPath(root);
        if (Directory.Exists(Root)) throw new IOException("Choose a fresh authoring polish evidence root.");
        Directory.CreateDirectory(Root);
        Environment.SetEnvironmentVariable("AXIOM_STATE_ROOT", Path.Combine(Root, "state"));
    }

    internal static async Task StartAsync(MainWindow window)
    {
        try
        {
            await window.StartupCompleted;
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            var workspace = Path.Combine(Root!, "workspace"); Directory.CreateDirectory(workspace);
            const string body = "# Findings\n\nOrdinary formatted **text** before callouts.\n\n> [!note] Note\n> Test **bold**\n\n>[!warning] Warning\n> OH NO!\n\n```csharp\npublic string GetString(int index)\n{\n\t// comment\n\treturn \"value\" + 42;\n}\n```\n\n```c#\npublic int Value = 7;\n```\n";
            var source = DocumentIdCodec.Enroll(body, Guid.NewGuid());
            var path = Path.Combine(workspace, "polish.md"); await File.WriteAllTextAsync(path, source);
            await window.SwitchWorkspaceAsync(workspace); await window.OpenDocumentAsync(path);
            var editor = window.FindControl<TextEditor>("Editor")!;
            var preview = window.FindControl<NativeWebView>("Preview")!;
            await Task.Delay(1400);
            for (var attempt = 0; attempt < 40 && await preview.InvokeScript("document.querySelectorAll('.callout-icon').length === 2") != "true"; attempt++) await Task.Delay(100);
            Require(await preview.InvokeScript("document.querySelectorAll('.callout-icon').length === 2 && !document.body.textContent.includes('[!note]') && !document.body.textContent.includes('[!warning]') && document.querySelector('.markdown-alert-note').textContent.includes('Test bold') && document.querySelector('.markdown-alert-warning').textContent.includes('OH NO!')") == "true", "Actual Preview callout markers/icons/body failed.");
            Require(await preview.InvokeScript("document.querySelectorAll('code.language-csharp').length === 2 && document.querySelectorAll('.csharp-keyword').length >= 6 && document.querySelector('.csharp-comment') && document.querySelector('.csharp-string') && document.querySelector('.csharp-number') ? true : false") == "true", "Actual Preview C# colouring failed.");
            Require(window.CSharpTokensForTesting.Count >= 10 && window.CSharpTokensForTesting.Any(token => source.Substring(token.Offset, token.Length) == "public"), "Editor C# colouring failed.");
            foreach (var appearance in new[] { AppearancePreference.Light, AppearancePreference.Dark })
            {
                await window.ApplyAppearanceForTestingAsync(appearance); await Task.Delay(250);
                Capture(window, appearance + "-csharp-source-surface.png");
                await File.WriteAllTextAsync(Path.Combine(Root!, appearance + "-preview-evidence.json"), await preview.InvokeScript("JSON.stringify({body: document.body.innerHTML, keywordColour: getComputedStyle(document.querySelector('.csharp-keyword')).color, source: document.querySelector('code.language-csharp').textContent})"));
            }
            await window.ExecuteCommandAsync("view.metadata"); await window.RefreshClassificationAsync();
            window.Activate(); window.UpdateLayout(); await Task.Delay(200);
            var metadata = window.FindControl<TabItem>("MetadataTab")!;
            foreach (var field in new[] { "Tags", "Type", "Aliases" })
            {
                var info = metadata.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Tag, field));
                await NativeProbeInput.ClickAsync(info);
                Require(window.FieldHelpFlyoutForTesting is { IsOpen: true, Content: TextBlock text } && text.Text == FieldHelp.Text(field), "Native field help click failed: " + field);
                window.FieldHelpFlyoutForTesting!.Hide(); await Task.Delay(100);
            }
            Capture(window, "metadata-labels-surface.png");
            await window.ExecuteCommandAsync("view.metadata");
            editor.Document.Insert(editor.Text.Length, "\nUnsaved tail"); await window.ProtectIfNeededForTestingAsync();
            var dirty = editor.Text;
            await NativeProbeInput.ClickAsync(editor.TextArea.TextView, right: true);
            Require(window.OpenEditorContextMenuForTesting is { IsOpen: true } menu && menu.Items.OfType<MenuItem>().Count() == 6, "Native editor context menu did not show six groups.");
            window.OpenEditorContextMenuForTesting!.Close();
            Require(editor.Text == dirty && await File.ReadAllTextAsync(path) == source && editor.Document.UndoStack.CanUndo, "Help/context/source highlighting changed Markdown or Undo.");
            editor.Undo(); Require(editor.Text == source, "Dirty tail could not undo in one step.");
            window.SetEditorTextForTesting("10. Parent\n11. Child"); editor.Select(editor.Text.Length, 0); editor.TextArea.Focus(); window.Activate(); await Task.Delay(100);
            NativeProbeInput.Key(0x09); await Task.Delay(150);
            Require(editor.Text == "10. Parent\n    1. Child", "Actual Tab did not create a valid ordered sublist.");
            NativeProbeInput.Key(0x09, shift: true); await Task.Delay(150);
            Require(editor.Text == "10. Parent\n11. Child", "Actual Shift+Tab did not restore parent numbering.");
            editor.Undo(); Require(editor.Text == "10. Parent\n    1. Child", "Outdent did not undo in one step.");
            await File.WriteAllTextAsync(Path.Combine(Root!, "result.json"), JsonSerializer.Serialize(new
            {
                Version = window.BuildIdentityForTesting.DisplayText
              , NativeFieldInfoLabelsAndClicks = true
              , NativeGroupedContextMenu = true
              , NativeTabShiftTabOrderedNestingAndUndo = true
              , ActualWebViewCSharpAliasesAndColours = true
              , EditorCSharpTokensLightAndDark = true
              , ActualWebViewCalloutIconsWithoutMarkers = true
              , SavedDirtyIdentityRecoveryUndoPreserved = true
              , HumanChecks = "Final visual comfort, DPI/accessibility and release acceptance remain Ben's checks. Surface captures omit hosted WebView; separate DOM evidence records Preview."
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error) { await File.WriteAllTextAsync(Path.Combine(Root!, "error.txt"), error.ToString()); }
        finally { window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard"); window.Close(); }
    }

    private static void Capture(MainWindow window, string name)
    {
        window.UpdateLayout();
        using var image = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height), new Vector(96, 96));
        image.Render(window); image.Save(Path.Combine(Root!, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
    private static void Require(bool value, string message) { if (!value) throw new IOException(message); }
}
