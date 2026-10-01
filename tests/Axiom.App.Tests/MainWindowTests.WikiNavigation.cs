using Avalonia.Headless.XUnit;
using Axiom.Core;
using Xunit;

namespace Axiom.App.Tests;

public sealed partial class MainWindowTests
{
    [AvaloniaFact]
    public async Task WikiNavigationAndBacklinksUseDirtyBuffersWithoutSavingOrEnrolling()
    {
        var root = CreateTabWorkspace();
        try
        {
            var source = Path.Combine(root, "source.md");
            var target = Path.Combine(root, "target.md");
            const string savedSource = "# Source\n\n[[Target#Live heading|Go]]\n";
            await File.WriteAllTextAsync(source, savedSource);
            await File.WriteAllTextAsync(target, "# Target\n\n## Saved heading\n");
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(target);
            window.SetEditorTextForTesting("# Target\n\n## Live heading\n\nUnsaved target\n");
            var targetTab = window.TabsForTesting[0];
            await window.OpenDocumentAsync(source);
            var sourceTab = window.TabsForTesting[1];
            window.SetEditorTextForTesting(savedSource + "Unsaved source\n");
            window.SetEditorSelectionForTesting(savedSource.IndexOf("[[", StringComparison.Ordinal) + 3, 0);
            await window.FollowEditorLinkForTestingAsync();
            Assert.Equal(target, window.PathContextForTesting);
            Assert.Equal(window.StructureForTesting.Headings[1].Source.Offset, window.EditorCaretOffsetForTesting);
            Assert.Same(targetTab, window.TabsForTesting[0]);
            Assert.True(sourceTab.Session.IsDirty);
            Assert.True(targetTab.Session.IsDirty);
            await window.RebuildBacklinksForTestingAsync();
            var incoming = Assert.Single(window.BacklinksForTesting);
            Assert.True(incoming.IsUnsaved);
            Assert.Equal(MarkdownLinkResolutionStatus.Resolved, incoming.Status);
            await window.OpenBacklinkForTestingAsync(incoming);
            Assert.Equal(source, window.PathContextForTesting);
            Assert.Equal(incoming.Source.Offset, window.EditorCaretOffsetForTesting);
            Assert.Equal(savedSource, await File.ReadAllTextAsync(source));
            Assert.Equal("# Target\n\n## Saved heading\n", await File.ReadAllTextAsync(target));
            await window.FollowLinkForTestingAsync(WikiLinkResolver.Prefix + Uri.EscapeDataString("missing"));
            Assert.Equal(source, window.PathContextForTesting);
            Assert.False(File.Exists(Path.Combine(root, "missing.md")));
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            await window.ConfirmAllTabsForTestingAsync();
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }
}
