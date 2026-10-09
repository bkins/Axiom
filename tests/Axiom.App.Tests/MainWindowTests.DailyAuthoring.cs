using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using Axiom.Core;
using Axiom.Infrastructure;
using Xunit;

namespace Axiom.App.Tests;

public sealed partial class MainWindowTests
{
    [AvaloniaFact]
    public async Task ShiftAltDownOnlyInsertsAtBothRowsAndUndoesOnce()
    {
        var root = CreateTabWorkspace();
        try
        {
            var window = CreateWindow(root);
            window.FindControl<Grid>("EditorPreviewLayout")!.Children.Remove(window.FindControl<NativeWebView>("Preview")!);
            window.Show(); await window.StartupCompleted;
            var editor = window.FindControl<TextEditor>("Editor")!;
            var source = "abcd\nefgh\nijkl\n";
            window.SetEditorTextForTesting(source);
            editor.CaretOffset = 1; editor.TextArea.Focus(); window.UpdateLayout();
            editor.TextArea.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down, KeyModifiers = KeyModifiers.Alt | KeyModifiers.Shift });
            Assert.IsType<RectangleSelection>(editor.TextArea.Selection);
            editor.TextArea.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "X" });
            Assert.Equal("aXbcd\neXfgh\nijkl\n", window.EditorTextForTesting);
            window.UndoEditorForTesting();
            Assert.Equal(source, window.EditorTextForTesting);
            await CloseWorkbenchTestAsync(window);
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task ShiftAltSelectionUsesExistingRectangleEditingAndUndo()
    {
        var root = CreateTabWorkspace();
        try
        {
            var window = CreateWindow(root);
            window.FindControl<Grid>("EditorPreviewLayout")!.Children.Remove(window.FindControl<NativeWebView>("Preview")!);
            window.Show(); await window.StartupCompleted;
            var editor = window.FindControl<TextEditor>("Editor")!;
            window.SetEditorTextForTesting("abcd\nefgh\nijkl\n");
            editor.CaretOffset = 1; editor.TextArea.Focus(); window.UpdateLayout();
            foreach (var key in new[] { Key.Down, Key.Right })
                editor.TextArea.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = KeyModifiers.Alt | KeyModifiers.Shift });
            Assert.IsType<RectangleSelection>(editor.TextArea.Selection);
            Assert.Equal(2, editor.TextArea.Selection.Segments.Count());
            editor.TextArea.Selection.ReplaceSelectionWithText("X");
            Assert.Equal("aXcd\neXgh\nijkl\n", window.EditorTextForTesting);
            window.UndoEditorForTesting();
            Assert.Equal("abcd\nefgh\nijkl\n", window.EditorTextForTesting);
            var enrolled = DocumentIdCodec.Enroll("abcd\nefgh\n", Guid.NewGuid());
            window.SetEditorTextForTesting(enrolled);
            editor.TextArea.Selection = new RectangleSelection(editor.TextArea, new TextViewPosition(1, 3), new TextViewPosition(2, 4));
            editor.TextArea.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "X" });
            Assert.Equal(enrolled, window.EditorTextForTesting);
            await CloseWorkbenchTestAsync(window);
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task ReorderingDirtyTabsRetainsEditorUndoRecoveryAndRememberedOrder()
    {
        var root = CreateTabWorkspace();
        try
        {
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            foreach (var name in new[] { "a.md", "b.md", "c.md" })
            {
                var path = Path.Combine(root, name); await File.WriteAllTextAsync(path, "# Saved\n\ncontent");
                await window.OpenDocumentAsync(path);
            }
            var active = window.TabsForTesting.Last();
            window.SetEditorSelectionForTesting(9, 7); window.ApplyMarkdownEditForTesting(MarkdownEditKind.Bold);
            var dirty = window.EditorTextForTesting;
            var document = active.Document;
            await window.ProtectIfNeededForTestingAsync();
            await window.ReorderTabForTestingAsync(active, 0);
            Assert.Same(active, window.TabsForTesting[0]);
            Assert.Same(document, window.FindControl<TextEditor>("Editor")!.Document);
            Assert.Equal(dirty, window.EditorTextForTesting);
            Assert.Equal("content", window.EditorSelectedTextForTesting);
            var state = new SqliteStateStore(new AppDataPaths(Path.Combine(root, "state")));
            var workspace = (await state.GetActiveWorkspaceAsync())!;
            var remembered = (await state.GetWorkspaceSessionAsync(workspace.WorkspaceId))!;
            Assert.Equal(new[] { "c.md", "a.md", "b.md" }, remembered.Documents.Select(location => location.RelativePath));
            Assert.Contains(await state.GetRecoverableDraftsAsync(), draft => draft.Text == dirty);
            Assert.Equal("# Saved\n\ncontent", await File.ReadAllTextAsync(active.Session.Path!));
            window.UndoEditorForTesting(); Assert.Equal("# Saved\n\ncontent", window.EditorTextForTesting);
            await CloseWorkbenchTestAsync(window);
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task ExplicitNewDocumentGetsOneIdentityAndRecoveryRetainsIt()
    {
        var root = CreateTabWorkspace();
        try
        {
            var window = CreateWindow(root); await window.LoadFolderAsync(root);
            await window.ExecuteCommandAsync("document.new");
            var tab = window.TabsForTesting.Last();
            var identity = Assert.IsType<Guid>(tab.Session.DocumentId);
            Assert.True(tab.Session.IsDirty);
            window.SetEditorTextForTesting(tab.Session.Text + "# New text\n");
            await window.ProtectIfNeededForTestingAsync();
            var state = new SqliteStateStore(new AppDataPaths(Path.Combine(root, "state")));
            var draft = Assert.Single(await state.GetRecoverableDraftsAsync());
            Assert.Equal(identity, DocumentSession.Recover(draft).DocumentId);
            await CloseWorkbenchTestAsync(window);
        }
        finally { Directory.Delete(root, true); }
    }
}
