using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Axiom.Core;
using Axiom.Infrastructure;
using Xunit;

namespace Axiom.App.Tests;

public sealed partial class MainWindowTests
{
    [AvaloniaFact]
    public async Task EditorPreferencesRetainDirtyTabsSelectionUndoAndRecovery()
    {
        var root = CreateTabWorkspace();
        try
        {
            var path = Path.Combine(root, "one.md");
            await File.WriteAllTextAsync(path, "saved source");
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(path);
            var tab = window.TabsForTesting[0];
            tab.Document.Insert(tab.Document.TextLength, " unsaved");
            window.SetEditorSelectionForTesting(2, 4);
            var caret = window.EditorCaretOffsetForTesting;
            window.ApplyEditorPreferencesForTesting(new("Consolas", 23, false, 7));
            Assert.Equal("saved source unsaved", window.EditorTextForTesting);
            Assert.Equal(caret, window.EditorCaretOffsetForTesting);
            Assert.Equal("ved ", window.EditorSelectedTextForTesting);
            Assert.True(tab.Document.UndoStack.CanUndo);
            Assert.True(tab.Session.IsDirty);
            await window.ProtectIfNeededForTestingAsync();
            var state = new SqliteStateStore(new AppDataPaths(Path.Combine(root, "state")));
            Assert.Contains(await state.GetRecoverableDraftsAsync(), draft => draft.Text == "saved source unsaved");
            Assert.Equal("saved source", await File.ReadAllTextAsync(path));
            window.SetEditorTextForTesting("saved source");
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task SuccessfulOpenRecordsRecentsButFailedOpenDoesNot()
    {
        var root = CreateTabWorkspace();
        try
        {
            var path = Path.Combine(root, "one.md");
            await File.WriteAllTextAsync(path, "saved");
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(path);
            await window.OpenDocumentAsync(Path.Combine(root, "missing.md"));
            var state = new SqliteStateStore(new AppDataPaths(Path.Combine(root, "state")));
            Assert.Equal(path, Assert.Single(await state.GetRecentDocumentsAsync()).Path);
            await window.OpenDocumentAsync(path);
            Assert.Single(await state.GetRecentDocumentsAsync());
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }
}
