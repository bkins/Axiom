using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using AvaloniaEdit;
using Axiom.Core;

namespace Axiom.App;

internal static class UsageReliabilityProbe
{
    internal static string? Root { get; private set; }

    internal static void Configure(string root)
    {
        Root = Path.GetFullPath(root);
        if (Directory.Exists(Root)) throw new IOException("Choose a fresh reliability evidence directory.");
        Directory.CreateDirectory(Root);
        Environment.SetEnvironmentVariable("AXIOM_STATE_ROOT", Path.Combine(Root, "state"));
    }

    internal static async Task StartAsync(MainWindow window)
    {
        var root = Root!;
        try
        {
            await window.StartupCompleted;
            window.Width = 1200;
            window.Height = 720;
            var path = Path.Combine(root, "long.md");
            var source = string.Join("\n\n", Enumerable.Range(1, 100).Select(index => $"Paragraph {index}: " + new string('x', 180)));
            await File.WriteAllTextAsync(path, source);
            await window.SwitchWorkspaceAsync(root);
            await window.OpenDocumentAsync(path);
            var editor = window.FindControl<TextEditor>("Editor")!;
            var preview = window.FindControl<NativeWebView>("Preview")!;
            await WaitAsync(async () => (await preview.InvokeScript("document.querySelectorAll('.source-block').length >= 100"))?.Trim().Trim('"') == "true");
            window.ApplyEditorPreferencesForTesting(new EditorPreferences { WordWrap = true });
            window.UpdateLayout();
            editor.TextArea.TextView.EnsureVisualLines();
            Require(editor.WordWrap && editor.Document.LineCount == 199, "Wrapping changed source line numbering.");
            Require(editor.TextArea.TextView.VisualLines.First().TextLines.Count > 1, "Long source line did not wrap into multiple visual rows.");
            window.ApplyEditorPreferencesForTesting(new EditorPreferences { WordWrap = false });
            Require(!editor.WordWrap && editor.Document.LineCount == 199, "Wrap-off did not apply.");
            // Long-line Enter must reveal the marker, not leave the old horizontal offset behind.
            window.SetEditorTextForTesting("- " + new string('x', 300));
            window.SetEditorSelectionForTesting(editor.Document.TextLength, 0);
            window.UpdateLayout();
            editor.TextArea.Caret.BringCaretToView();
            Require(editor.TextArea.TextView.HorizontalOffset > 0, "Long-list fixture did not scroll horizontally.");
            Require(window.HandleMarkdownListKeyForTesting(Key.Enter, KeyModifiers.None, true), "List continuation did not apply.");
            await Task.Delay(100);
            Require(editor.TextArea.TextView.HorizontalOffset < 1, "List marker stayed horizontally hidden.");
            window.SetEditorTextForTesting(source);
            await WaitAsync(async () => (await preview.InvokeScript("document.querySelectorAll('.source-block').length >= 100"))?.Trim().Trim('"') == "true");
            await Task.Delay(600);
            window.ToggleScrollSyncForTesting();
            await window.SynchronizeScrollForTestingAsync();
            await Task.Delay(600);
            ((Avalonia.Controls.Primitives.ILogicalScrollable)editor.TextArea.TextView).Offset =
                new Avalonia.Vector(0, editor.TextArea.TextView.DefaultLineHeight * 80);
            await window.SynchronizeScrollForTestingAsync();
            var forwardRatio = await preview.InvokeScript("document.scrollingElement.scrollTop / Math.max(1, document.scrollingElement.scrollHeight - window.innerHeight)");
            Require(MainWindow.TryParsePreviewScrollRatioForTesting(forwardRatio ?? "", out var ratio) && ratio > 0.2, "Editor scroll did not synchronize to Preview.");
            await window.SynchronizeScrollForTestingAsync();
            await WaitAsync(async () => (await preview.InvokeScript("!!window.__axiomReading"))?.Trim().Trim('"') == "true");
            await preview.InvokeScript("(() => { window.dispatchEvent(new WheelEvent('wheel')); window.scrollTo(0, (document.scrollingElement.scrollHeight - window.innerHeight) * 0.7); return true; })()");
            await Task.Delay(400);
            await window.SynchronizeScrollForTestingAsync();
            await File.WriteAllTextAsync(Path.Combine(root, "scroll-state.json"), JsonSerializer.Serialize(new
            {
                EditorVerticalOffset = editor.TextArea.TextView.VerticalOffset
              , LineHeight = editor.TextArea.TextView.DefaultLineHeight
              , PreviewState = await preview.InvokeScript("JSON.stringify({reading:window.__axiomReading,top:window.scrollY,range:document.scrollingElement.scrollHeight-window.innerHeight})")
            }));
            Require(editor.TextArea.TextView.VerticalOffset / Math.Max(1, editor.TextArea.TextView.DefaultLineHeight) > 100, "Preview scroll did not synchronize to editor.");
            window.ToggleScrollSyncForTesting();
            await window.ShowPersistentHelpForTestingAsync("Settings and editor preferences");
            window.UpdateLayout();
            var help = window.PersistentHelpForTesting!;
            var search = help.GetLogicalDescendants().OfType<TextBox>().Single(control => control.Name == "HelpSearch");
            Require(search.Focus(), "Help search cannot receive native focus.");
            search.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "Word" });
            await Task.Delay(500);
            Require(search.Text == "Word", "Native Help text input was lost.");
            Require(help.GetLogicalDescendants().OfType<ListBox>().Single(control => control.Name == "HelpSearchResults").ItemCount > 0, "Help search did not return results.");
            Require(editor.TextArea.Focus(), "Editor cannot regain focus while Help stays open.");
            editor.CaretOffset = editor.Document.TextLength;
            editor.TextArea.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "\nTyping with Help open." });
            await Task.Delay(900);
            Require(editor.IsKeyboardFocusWithin, "Preview refresh stole editor focus.");
            Require(editor.Text.EndsWith("Typing with Help open.", StringComparison.Ordinal), "Editor text input failed.");
            Require(await File.ReadAllTextAsync(path) == source, "Native probe changed saved Markdown.");
            await window.ProtectIfNeededForTestingAsync();
            var dirtyTab = window.TabsForTesting.Single(tab => tab.Session.Path == path);
            var dirtyText = dirtyTab.Session.Text;
            var forwardedPath = Path.Combine(root, "forwarded.markdown");
            await File.WriteAllTextAsync(forwardedPath, "# Forwarded document\n");
            await File.WriteAllTextAsync(Path.Combine(root, "activation-ready.txt"), forwardedPath);
            await WaitAsync(() => Task.FromResult(window.TabsForTesting.Count(tab => tab.Session.Path == forwardedPath) == 1));
            Require(dirtyTab.Session.IsDirty && dirtyTab.Session.Text == dirtyText, "Second-launch activation lost the original dirty tab.");
            Require(await File.ReadAllTextAsync(path) == source, "Forwarding silently saved the original document.");
            await File.WriteAllTextAsync(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new
            {
                Version = window.BuildIdentityForTesting.DisplayText
              , WrapAppliesWithoutChangingLogicalLines = true
              , ListMarkerVisibleAfterHorizontalContinuation = true
              , BidirectionalNativeScrollSynchronization = true
              , HelpSearchRoutedInputAndResults = true
              , EditorFocusSurvivesLiveRefreshWithHelpOpen = true
              , SavedSourceUnchanged = true
              , SecondLaunchForwardsToOneTabAndPreservesDirtySource = true
              , HumanPhysicalTypingAcceptance = false
            }, new JsonSerializerOptions { WriteIndented = true }));
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            window.Close();
        }
        catch (Exception exception)
        {
            await File.WriteAllTextAsync(Path.Combine(root, "error.txt"), exception.ToString());
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            window.Close();
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new IOException(message);
    }

    private static async Task WaitAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!await condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Native reliability condition was not reached.");
            await Task.Delay(50);
        }
    }
}
