using Avalonia.Headless.XUnit;
using Xunit;

namespace Axiom.App.Tests;

public sealed partial class MainWindowTests
{
    [AvaloniaFact]
    public async Task UnsavedOverlayReplacesSavedReferencesAndDiscardRestoresIndexWithoutFileChanges()
    {
        var root = CreateTabWorkspace();
        try
        {
            var source = Path.Combine(root, "source.md");
            var target = Path.Combine(root, "target.md");
            const string saved = "# Context\n\n[Go](target.md)\n";
            await File.WriteAllTextAsync(source, saved);
            await File.WriteAllTextAsync(target, "# Target\n");
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(source);
            var sourceTab = window.TabsForTesting[0];
            await window.OpenDocumentAsync(target);
            await window.RebuildBacklinksForTestingAsync();
            Assert.False(Assert.Single(window.BacklinksForTesting).IsUnsaved);
            await window.SelectTabForTestingAsync(sourceTab);
            window.SetEditorTextForTesting("# Context\n\n[New](target.md#target)\n[More](target.md)\n");
            await window.SelectTabForTestingAsync(window.TabsForTesting[1]);
            Assert.Equal(2, window.BacklinksForTesting.Count);
            Assert.All(window.BacklinksForTesting, reference => Assert.True(reference.IsUnsaved));
            var reference = window.BacklinksForTesting[0];
            await window.OpenBacklinkForTestingAsync(reference);
            Assert.Equal(source, window.PathContextForTesting);
            Assert.Equal(reference.Source.Offset, window.EditorCaretOffsetForTesting);
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            await window.ConfirmAllTabsForTestingAsync();
            await window.SelectTabForTestingAsync(window.TabsForTesting[1]);
            Assert.False(Assert.Single(window.BacklinksForTesting).IsUnsaved);
            Assert.Equal(saved, await File.ReadAllTextAsync(source));
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task ChangedSourceRelocatesUniqueReferenceAndRejectsAmbiguousStaleOffsets()
    {
        var root = CreateTabWorkspace();
        try
        {
            var source = Path.Combine(root, "source.md");
            var target = Path.Combine(root, "target.md");
            await File.WriteAllTextAsync(source, "[Go](target.md)\n");
            await File.WriteAllTextAsync(target, "target");
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(target);
            await window.RebuildBacklinksForTestingAsync();
            var original = Assert.Single(window.BacklinksForTesting);
            await File.WriteAllTextAsync(source, "Inserted text\n\n[Go](target.md)\n");
            await window.OpenBacklinkForTestingAsync(original);
            Assert.Equal(15, window.EditorCaretOffsetForTesting);
            await window.SelectTabForTestingAsync(window.TabsForTesting[0]);
            await File.WriteAllTextAsync(source, "[First](target.md)\n[Second](target.md)\n");
            await window.HandleExternalChangeForTestingAsync(source);
            await window.OpenBacklinkForTestingAsync(original);
            Assert.Equal(target, window.PathContextForTesting);
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }
}
