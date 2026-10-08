using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Axiom.App.Tests;

public sealed partial class MainWindowTests
{
    [AvaloniaFact]
    public async Task RefreshRevealsActiveNestedFileInsteadOfPreviousFolderWithoutSavingDirtyBuffer()
    {
        var root = CreateTabWorkspace();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "nested", "deep"));
            Directory.CreateDirectory(Path.Combine(root, "other"));
            await File.WriteAllTextAsync(Path.Combine(root, "other", "anchor.md"), "Anchor");
            var path = Path.Combine(root, "nested", "deep", "note.md");
            await File.WriteAllTextAsync(path, "Saved");
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(path);
            window.SetEditorTextForTesting("Unsaved");
            var tree = window.FindControl<TreeView>("FileTree")!;
            tree.SelectedItem = window.ExplorerNodesForTesting.Single(node => node.Name == "other");
            window.FilterExplorerForTesting("");
            Assert.Equal(Path.GetRelativePath(root, path), Assert.IsType<WorkspaceExplorerNode>(tree.SelectedItem).RelativePath);
            Assert.Equal("Unsaved", window.EditorTextForTesting);
            Assert.Equal("Saved", await File.ReadAllTextAsync(path));
            await CloseWorkbenchTestAsync(window);
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task OpeningAndSwitchingTabsRevealsCanonicalFileWithoutExpandingOutlineOrClearingFilter()
    {
        var root = CreateTabWorkspace();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "nested", "deep"));
            var path = Path.Combine(root, "nested", "deep", "note.md");
            await File.WriteAllTextAsync(path, "# Source\n");
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(path);
            var tree = window.FindControl<TreeView>("FileTree")!;
            var selected = Assert.IsType<WorkspaceExplorerNode>(tree.SelectedItem);
            Assert.Equal(Path.GetRelativePath(root, path), selected.RelativePath);
            Assert.False(selected.IsExpanded);
            Assert.True(window.ExplorerNodesForTesting.Single(n => n.Name == "nested").IsExpanded);
            await window.NewTabForTestingAsync();
            window.FilterExplorerForTesting("unrelated");
            await window.SelectTabForTestingAsync(window.TabsForTesting.Single(t => t.Session.Path == path));
            Assert.Equal("unrelated", window.FindControl<TextBox>("ExplorerFilter")!.Text);
            Assert.Contains("hidden by filter", window.FindControl<TextBlock>("ExplorerFilterStatus")!.Text);
            Assert.Equal("# Source\n", await File.ReadAllTextAsync(path));
            await CloseWorkbenchTestAsync(window);
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task NewUsesSelectedFolderAndPinsShareExplorerWithoutSavingDraft()
    {
        var root = CreateTabWorkspace();
        try
        {
            var folder = Path.Combine(root, "notes");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "note.md");
            await File.WriteAllTextAsync(path, "# Source\n");
            var window = CreateWindow(root);
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(path);
            await window.ToggleCurrentPinForTestingAsync();
            var group = window.ExplorerNodesForTesting[0];
            Assert.True(group.IsGroup);
            Assert.True(Assert.Single(group.Children).IsPinned);
            Assert.Single(window.WorkspacePinsForTesting);
            var tree = window.FindControl<TreeView>("FileTree")!;
            tree.SelectedItem = window.ExplorerNodesForTesting.Single(n => n.Name == "notes");
            await window.ExecuteCommandAsync("document.new");
            var draft = window.TabsForTesting.Last();
            Assert.Null(draft.Session.Path);
            Assert.Equal(folder, draft.SuggestedSaveFolder);
            Assert.NotNull(draft.Session.DocumentId);
            Assert.True(draft.Session.IsDirty);
            Assert.Empty(Axiom.Core.DocumentIdCodec.RemoveCanonicalMarkerForPreview(draft.Document.Text).Trim());
            Assert.False(File.Exists(Path.Combine(folder, "Untitled.md")));
            Assert.Equal("# Source\n", await File.ReadAllTextAsync(path));
            await CloseWorkbenchTestAsync(window);
        }
        finally { Directory.Delete(root, true); }
    }
}
