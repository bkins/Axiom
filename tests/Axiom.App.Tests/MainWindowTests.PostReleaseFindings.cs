using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Axiom.Core;
using Axiom.Infrastructure;
using Xunit;

namespace Axiom.App.Tests;

public sealed partial class MainWindowTests
{
    [AvaloniaFact]
    public async Task ControlSSavesOnlyCurrentTabAndLeavesInactiveDraftDirty()
    {
        var root = CreateTabWorkspace();
        try
        {
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            var first = Path.Combine(root, "first.md");
            var second = Path.Combine(root, "second.md");
            foreach (var path in new[] { first, second }) await File.WriteAllTextAsync(path, "saved");
            await window.OpenDocumentAsync(first);
            window.TabsForTesting[0].Document.Insert(5, " first draft");
            await window.OpenDocumentAsync(second);
            window.TabsForTesting[1].Document.Insert(5, " second draft");
            window.ConfigureShortcutsForTesting(new());
            Assert.True(await window.ExecuteShortcutForTestingAsync(new KeyEventArgs { Key = Key.S, KeyModifiers = KeyModifiers.Control }));
            Assert.Equal("saved", await File.ReadAllTextAsync(first));
            Assert.True(window.TabsForTesting[0].Session.IsDirty);
            Assert.Equal("saved second draft", await File.ReadAllTextAsync(second));
            Assert.False(window.TabsForTesting[1].Session.IsDirty);
            Assert.True(window.EditorCanUndoForTesting);
            await CloseWorkbenchTestAsync(window);
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task SwitchingTabsClearsSpellingOffsetsAndRechecksEachDocument()
    {
        var root = CreateTabWorkspace();
        try
        {
            var first = Path.Combine(root, "first.md");
            var second = Path.Combine(root, "second.md");
            await File.WriteAllTextAsync(first, "This is speling.");
            await File.WriteAllTextAsync(second, "This is a word.");
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(first);
            window.ApplyEditorPreferencesForTesting(new EditorPreferences { SpellCheck = true });
            for (var attempt = 0; attempt < 40 && window.SpellingIssuesForTesting.Count == 0; attempt++) await Task.Delay(50);
            Assert.Equal("speling", Assert.Single(window.SpellingIssuesForTesting).Word);
            await window.OpenDocumentAsync(second);
            Assert.Empty(window.SpellingIssuesForTesting);
            await Task.Delay(650);
            Assert.Empty(window.SpellingIssuesForTesting);
            await window.SelectTabForTestingAsync(window.TabsForTesting[0]);
            Assert.Empty(window.SpellingIssuesForTesting);
            for (var attempt = 0; attempt < 40 && window.SpellingIssuesForTesting.Count == 0; attempt++) await Task.Delay(50);
            Assert.Equal("speling", Assert.Single(window.SpellingIssuesForTesting).Word);
            Assert.Equal("This is speling.", await File.ReadAllTextAsync(first));
            await CloseWorkbenchTestAsync(window);
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task PinnedLabelsUseFilenameWhileRetainingDistinctPaths()
    {
        var root = CreateTabWorkspace();
        try
        {
            var window = CreateWindow(root);
            foreach (var folder in new[] { "one", "two" })
            {
                Directory.CreateDirectory(Path.Combine(root, folder));
                await File.WriteAllTextAsync(Path.Combine(root, folder, "same.md"), "saved");
            }
            await window.LoadFolderAsync(root);
            foreach (var folder in new[] { "one", "two" })
            {
                await window.OpenDocumentAsync(Path.Combine(root, folder, "same.md"));
                Assert.Equal(CommandOutcome.Completed, (await window.ExecuteCommandAsync("document.pin")).Outcome);
            }
            var pins = Assert.Single(window.ExplorerNodesForTesting, node => node.IsGroup).Children;
            Assert.Equal(2, pins.Count);
            Assert.All(pins, pin => { Assert.Equal("same.md", pin.Name); Assert.Contains(pin.RelativePath, pin.Description); });
            Assert.NotEqual(pins[0].RelativePath, pins[1].RelativePath);
            await window.OpenExplorerNodeForTestingAsync(pins[0]);
            Assert.Equal(Path.Combine(root, pins[0].RelativePath), window.TabsForTesting.Single(tab => tab.Session.Path == Path.Combine(root, pins[0].RelativePath)).Session.Path);
            await CloseWorkbenchTestAsync(window);
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public void GitPanelSeparatesWorkingStagedAndUnsupportedRows()
    {
        var files = new[]
        {
            new GitChangedFile("note.md", "note.md", "M", " ", null, true, null)
          , new GitChangedFile("other.MARKDOWN", "other.MARKDOWN", " ", "M", null, true, null)
          , new GitChangedFile("renamed.md", "renamed.md", "R", " ", "old.md", false, "Review rename in external Git.")
          , new GitChangedFile("image.png", "image.png", "?", "?", null, false, "Not Markdown.")
          , new GitChangedFile("settings.json", "settings.json", "M", " ", null, false, "Not Markdown.")
          , new GitChangedFile("partial.md", "partial.md", "M", "M", null, true, null)
        };
        var snapshot = new GitRepositorySnapshot("C:/workspace", "C:/workspace", "refs/heads/main", null, "index", "hash", files, true, null);
        var view = new WorkspaceGitView(new Window(), new GitWorkspaceService(), snapshot, _ => false, () => true, () => { });
        Assert.Equal(new[] { "other.MARKDOWN", "partial.md" }, view.ChangedFiles.Items.Cast<GitChangedFile>().Select(file => file.RelativePath));
        Assert.Equal(new[] { "note.md", "partial.md" }, view.StagedFiles.Items.Cast<GitChangedFile>().Select(file => file.RelativePath));
        Assert.Equal(new[] { "renamed.md", "image.png", "settings.json" }, view.UnsupportedFiles.Items.Cast<GitChangedFile>().Select(file => file.RelativePath));
        Assert.IsNotType<ListBox>(view.UnsupportedFiles);
        Assert.Equal(6, snapshot.Files.Count);
    }
}
