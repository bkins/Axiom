using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using Xunit;

namespace Axiom.App.Tests;

public sealed partial class MainWindowTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RectangularSelectionSurvivesTabRoundTripAndTypesWithOneUndo(bool verticalOnly, bool reverse)
    {
        var root = CreateTabWorkspace();
        try
        {
            var window = CreateWindow(root);
            window.FindControl<Grid>("EditorPreviewLayout")!.Children.Remove(window.FindControl<NativeWebView>("Preview")!);
            window.Show();
            await window.StartupCompleted;
            var firstPath = Path.Combine(root, "first.md");
            var secondPath = Path.Combine(root, "second.md");
            const string source = "abcd\nefgh\nijkl\n";
            await File.WriteAllTextAsync(firstPath, source);
            await File.WriteAllTextAsync(secondPath, "Other document\n");
            await window.OpenDocumentAsync(firstPath);
            var first = window.TabsForTesting.Last();
            var editor = window.FindControl<TextEditor>("Editor")!;
            editor.CaretOffset = reverse ? (verticalOnly ? 6 : 7) : 1;
            editor.TextArea.Focus();
            window.UpdateLayout();
            var keys = verticalOnly ? new[] { reverse ? Key.Up : Key.Down }
                : new[] { reverse ? Key.Up : Key.Down, reverse ? Key.Left : Key.Right };
            foreach (var key in keys)
                editor.TextArea.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = KeyModifiers.Alt | KeyModifiers.Shift });
            var before = Assert.IsType<RectangleSelection>(editor.TextArea.Selection);
            var segments = before.Segments.Select(segment => (segment.StartOffset, segment.Length)).ToArray();
            var caret = editor.CaretOffset;
            await window.OpenDocumentAsync(secondPath);
            await window.SelectTabForTestingAsync(first);
            var restored = Assert.IsType<RectangleSelection>(editor.TextArea.Selection);
            Assert.Equal(segments, restored.Segments.Select(segment => (segment.StartOffset, segment.Length)).ToArray());
            Assert.Equal(caret, editor.CaretOffset);
            editor.TextArea.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "X" });
            Assert.Equal(verticalOnly ? "aXbcd\neXfgh\nijkl\n" : "aXcd\neXgh\nijkl\n", window.EditorTextForTesting);
            window.UndoEditorForTesting();
            Assert.Equal(source, window.EditorTextForTesting);
            Assert.Equal(source, await File.ReadAllTextAsync(firstPath));
            await CloseWorkbenchTestAsync(window);
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task ReverseSelectionRetainsAnchorAndCaretAcrossTabRoundTrip()
    {
        var root = CreateTabWorkspace();
        try
        {
            var window = CreateWindow(root);
            window.FindControl<Grid>("EditorPreviewLayout")!.Children.Remove(window.FindControl<NativeWebView>("Preview")!);
            window.Show();
            await window.StartupCompleted;
            await window.LoadFolderAsync(root);
            var path = Path.Combine(root, "first.md");
            await File.WriteAllTextAsync(path, "abcd\nefgh\n");
            await window.OpenDocumentAsync(path);
            var first = window.TabsForTesting.Last();
            var editor = window.FindControl<TextEditor>("Editor")!;
            editor.TextArea.Selection = Selection.Create(editor.TextArea, 7, 1);
            editor.CaretOffset = 1;
            await window.ExecuteCommandAsync("document.new");
            await window.SelectTabForTestingAsync(first);
            Assert.Equal(7, editor.Document.GetOffset(editor.TextArea.Selection.StartPosition.Location));
            Assert.Equal(1, editor.Document.GetOffset(editor.TextArea.Selection.EndPosition.Location));
            Assert.Equal(1, editor.CaretOffset);
            Assert.Equal("bcd\nef", editor.SelectedText);
            await window.ExecuteCommandAsync("document.new");
            first.Document.Text = "x";
            await window.SelectTabForTestingAsync(first);
            Assert.Equal(1, editor.CaretOffset);
            Assert.Equal(0, editor.SelectionLength);
            Assert.Equal("x", editor.Text);
            Assert.Equal("abcd\nefgh\n", await File.ReadAllTextAsync(path));
            await CloseWorkbenchTestAsync(window);
        }
        finally { Directory.Delete(root, true); }
    }
}

