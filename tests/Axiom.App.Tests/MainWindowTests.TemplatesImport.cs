using Avalonia.Headless.XUnit;
using Axiom.Core;
using Axiom.Infrastructure;
using Xunit;

namespace Axiom.App.Tests;

public sealed partial class MainWindowTests
{
    [AvaloniaFact]
    public async Task TemplateCreationAndDroppedMarkdownKeepDirtyTabsAndTheirRecovery()
    {
        var root = CreateTabWorkspace();
        try
        {
            var path = Path.Combine(root, "original.md");
            await File.WriteAllTextAsync(path, "# Saved\n\ncontent");
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(path);
            window.SetEditorSelectionForTesting(9, 7);
            Assert.True(window.ApplyMarkdownEditForTesting(MarkdownEditKind.Bold));
            var dirtyText = window.EditorTextForTesting;
            var originalTab = window.TabsForTesting[0];
            await window.ProtectIfNeededForTestingAsync();
            var state = new SqliteStateStore(new AppDataPaths(Path.Combine(root, "state")));
            var workspace = await state.GetActiveWorkspaceAsync();
            Assert.NotNull(workspace);
            var expanded = DocumentTemplateService.Expand("# {{title}}\n{{date}}", "New", new DateOnly(2026, 10, 1));
            await window.CreateReviewedDocumentAsync(workspace, "new.md", expanded);
            await window.HandleDroppedFilesAsync([path, Path.Combine(root, "new.md"), path]);
            Assert.Equal(2, window.TabsForTesting.Count);
            Assert.Same(originalTab, window.TabsForTesting[0]);
            Assert.Equal(dirtyText, window.EditorTextForTesting);
            Assert.Equal("content", window.EditorSelectedTextForTesting);
            Assert.True(window.EditorCanUndoForTesting);
            Assert.Equal("# Saved\n\ncontent", await File.ReadAllTextAsync(path));
            var createdText = await File.ReadAllTextAsync(Path.Combine(root, "new.md"));
            Assert.NotNull(DocumentIdCodec.Find(createdText));
            Assert.Equal(expanded, DocumentIdCodec.RemoveCanonicalMarkerForPreview(createdText).TrimStart('\r', '\n'));
            Assert.Contains(await state.GetRecoverableDraftsAsync(), draft => draft.Text == dirtyText);
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }
}
