using Axiom.Core;

namespace Axiom.Infrastructure.Tests;

public sealed class WorkspaceFolderDiscoveryTests
{
    [Fact]
    public void EmptyLeavesRemainVisibleWhilePopulatedNonMarkdownBranchesStayHidden()
    {
        var root = Path.Combine(Path.GetTempPath(), "AxiomEmptyFolders-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "empty-parent", "empty-leaf"));
            Directory.CreateDirectory(Path.Combine(root, "assets"));
            Directory.CreateDirectory(Path.Combine(root, ".git", "refs", "tags"));
            File.WriteAllText(Path.Combine(root, "assets", "image.png"), "asset");
            File.WriteAllText(Path.Combine(root, "note.md"), "ordinary");
            var folders = WorkspaceFolderDiscovery.List(root);
            var tree = WorkspaceTreeBuilder.Build(root, [Path.Combine(root, "note.md")], folders);
            Assert.DoesNotContain(tree, node => node.Name == "assets");
            Assert.DoesNotContain(tree, node => node.Name == ".git");
            var empty = Assert.Single(tree, node => node.Name == "empty-parent");
            Assert.Empty(Assert.Single(empty.Children).Children);
            Assert.Single(tree, node => node.IsDocument);
            Assert.Equal(folders, WorkspaceFolderDiscovery.List(root));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
