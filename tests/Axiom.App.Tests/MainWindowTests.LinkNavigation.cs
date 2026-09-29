using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Axiom.Core;
using Xunit;

namespace Axiom.App.Tests;

public sealed partial class MainWindowTests
{
    [AvaloniaFact]
    public async Task StalePreviewLinksCannotNavigateToChangedTargets()
    {
        var root = CreateTabWorkspace();
        try
        {
            var path = Path.Combine(root, "source.md");
            await File.WriteAllTextAsync(path, "[Go](old.md)");
            await File.WriteAllTextAsync(Path.Combine(root, "old.md"), "old");
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(path);
            var rendered = window.StructureForTesting;
            window.SetEditorTextForTesting("[Go](new.md)");
            await window.FollowRenderedLinkForTestingAsync(new Uri("axiom-link://follow/current/0"), "current", rendered);
            Assert.Equal(path, window.PathContextForTesting);
            Assert.Contains("preview link changed", window.FindControl<TextBlock>("StatusText")!.Text);
            await window.FollowRenderedLinkForTestingAsync(new Uri("axiom-link://follow/previous/0"), "current", rendered);
            Assert.Single(window.TabsForTesting);
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            await window.ConfirmAllTabsForTestingAsync();
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task FollowingReferenceLinksAndHistoryRetainsDirtyBuffersAndNavigatesLiveHeadings()
    {
        var root = CreateTabWorkspace();
        try
        {
            var source = Path.Combine(root, "source.md");
            var target = Path.Combine(root, "target name.md");
            await File.WriteAllTextAsync(source, "# Source\n\n[Go][next]\n\n[next]: target%20name.md#live-heading\n");
            await File.WriteAllTextAsync(target, "# Saved heading\n\nTarget\n");
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(target);
            window.SetEditorTextForTesting("# Live heading\n\nTarget edits\n");
            var targetTab = window.TabsForTesting[0];
            await window.OpenDocumentAsync(source);
            window.SetEditorTextForTesting(window.EditorTextForTesting + "Source edits\n");
            window.SetEditorSelectionForTesting(12, 0);
            await window.FollowEditorLinkForTestingAsync();
            Assert.Equal(target, window.PathContextForTesting);
            Assert.Equal(0, window.EditorCaretOffsetForTesting);
            Assert.Equal(2, window.TabsForTesting.Count);
            Assert.Same(targetTab, window.TabsForTesting[0]);
            Assert.Equal("# Live heading\n\nTarget edits\n", window.EditorTextForTesting);
            await window.NavigateHistoryForTestingAsync(false);
            Assert.Equal(source, window.PathContextForTesting);
            Assert.Equal(12, window.EditorCaretOffsetForTesting);
            Assert.Contains("Source edits", window.EditorTextForTesting);
            await window.NavigateHistoryForTestingAsync(true);
            Assert.Equal(target, window.PathContextForTesting);
            Assert.Equal("# Saved heading\n\nTarget\n", await File.ReadAllTextAsync(target));
            Assert.All(window.TabsForTesting, tab => Assert.True(tab.Session.IsDirty));
            window.UnsavedChoiceForTesting = _ => Task.FromResult("Discard");
            await window.ConfirmAllTabsForTestingAsync();
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task UnsupportedAndMissingTargetsGiveFeedbackWithoutChangingTabsOrWritingMarkdown()
    {
        var root = CreateTabWorkspace();
        try
        {
            var path = Path.Combine(root, "source.md");
            await File.WriteAllTextAsync(path, "ordinary");
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(path);
            foreach (var destination in new[] { "https://example.com", "../outside.md", "photo.png", "missing.md", "source.md?query" })
            {
                await window.FollowLinkForTestingAsync(destination);
                Assert.Equal(path, window.PathContextForTesting);
                Assert.Single(window.TabsForTesting);
                Assert.DoesNotContain("Followed", window.FindControl<TextBlock>("StatusText")!.Text);
            }
            Assert.Equal("ordinary", await File.ReadAllTextAsync(path));
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task MissingHeadingRequiresDeliberateChoiceAndMissingHistoryIsPruned()
    {
        var root = CreateTabWorkspace();
        try
        {
            var source = Path.Combine(root, "source.md");
            var target = Path.Combine(root, "target.md");
            await File.WriteAllTextAsync(source, "source");
            await File.WriteAllTextAsync(target, "# Actual\n");
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(source);
            window.MissingHeadingChoiceForTesting = _ => Task.FromResult("Cancel");
            await window.FollowLinkForTestingAsync("target.md#missing");
            Assert.Equal(source, window.PathContextForTesting);
            window.MissingHeadingChoiceForTesting = _ => Task.FromResult("Open document");
            await window.FollowLinkForTestingAsync("target.md#missing");
            Assert.Equal(target, window.PathContextForTesting);
            File.Delete(source);
            await window.NavigateHistoryForTestingAsync(false);
            Assert.Equal(target, window.PathContextForTesting);
            Assert.False(window.FindControl<Button>("BackButton")!.IsEnabled);
            Assert.Contains("unavailable", window.FindControl<TextBlock>("StatusText")!.Text);
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }
}
