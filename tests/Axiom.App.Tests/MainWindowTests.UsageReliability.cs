using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using AvaloniaEdit;
using Axiom.Core;
using Xunit;

namespace Axiom.App.Tests;

public sealed partial class MainWindowTests
{
    [AvaloniaFact]
    public void ReapplyingWrapRepairsHorizontalScrollingWithoutChangingSourceOrUndo()
    {
        var root = CreateTabWorkspace();
        try
        {
            var window = CreateWindow(root);
            window.SetEditorTextForTesting("one\n" + new string('x', 200));
            var editor = window.FindControl<TextEditor>("Editor")!;
            editor.Document.Insert(editor.Document.TextLength, " dirty");
            editor.WordWrap = true;
            editor.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            var source = editor.Text;
            window.ApplyEditorPreferencesForTesting(new EditorPreferences { WordWrap = true });
            Assert.Equal(ScrollBarVisibility.Disabled, editor.HorizontalScrollBarVisibility);
            Assert.Equal(2, editor.Document.LineCount);
            Assert.Equal(source, editor.Text);
            Assert.True(editor.Document.UndoStack.CanUndo);
            window.ApplyEditorPreferencesForTesting(new EditorPreferences { WordWrap = false });
            Assert.Equal(ScrollBarVisibility.Auto, editor.HorizontalScrollBarVisibility);
            Assert.Equal(source, editor.Text);
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task ListContinuationRevealsTheFullMarkerAndCaret()
    {
        var root = CreateTabWorkspace();
        try
        {
            var window = CreateWindow(root);
            var layout = window.FindControl<Grid>("EditorPreviewLayout")!;
            layout.Children.Remove(window.FindControl<NativeWebView>("Preview")!);
            window.Show();
            await window.StartupCompleted;
            window.ApplyEditorPreferencesForTesting(new EditorPreferences { WordWrap = false });
            var editor = window.FindControl<TextEditor>("Editor")!;
            window.SetEditorTextForTesting("- " + new string('x', 300));
            window.SetEditorSelectionForTesting(editor.Document.TextLength, 0);
            window.UpdateLayout();
            editor.TextArea.Caret.BringCaretToView();
            Assert.True(editor.TextArea.TextView.HorizontalOffset > 0);
            Assert.True(window.HandleMarkdownListKeyForTesting(Key.Enter, KeyModifiers.None, true));
            Assert.Equal(0, editor.TextArea.TextView.HorizontalOffset);
            Assert.EndsWith("\n- ", editor.Text);
            Assert.Equal(2, editor.Document.LineCount);
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }
}
