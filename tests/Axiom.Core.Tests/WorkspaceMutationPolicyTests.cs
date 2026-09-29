namespace Axiom.Core.Tests;

public sealed class WorkspaceMutationPolicyTests
{
    [Fact]
    public void MovePickerRetainsNameAndAllowsWorkspaceRootOrNestedFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "workspace");
        Assert.Equal(Path.Combine("café", "☕.markdown"), WorkspaceMutationPolicy.MoveDestination(root, "old/☕.markdown", Path.Combine(root, "café")));
        Assert.Equal("☕.markdown", WorkspaceMutationPolicy.MoveDestination(root, "old/☕.markdown", root));
    }

    [Fact]
    public void MovePickerRejectsSiblingWorkspaceAndEscapingSource()
    {
        var root = Path.Combine(Path.GetTempPath(), "workspace");
        Assert.Throws<ArgumentException>(() => WorkspaceMutationPolicy.MoveDestination(root, "a.md", root + "-other"));
        Assert.Throws<ArgumentException>(() => WorkspaceMutationPolicy.MoveDestination(root, "../a.md", root));
    }

    [Theory]
    [InlineData("../escape.md")]
    [InlineData("nested/../escape.md")]
    [InlineData("CON.md")]
    [InlineData("COM1.md")]
    [InlineData("LPT².md")]
    [InlineData("folder./a.md")]
    [InlineData("folder /a.md")]
    [InlineData("a.txt")]
    [InlineData("a:stream.md")]
    [InlineData("a?.md")]
    [InlineData("/absolute.md")]
    public void RejectsUnsafeNamesAndPaths(string relative)
    {
        Assert.Throws<ArgumentException>(() => WorkspaceMutationPolicy.Resolve(Path.GetTempPath(), relative));
    }

    [Fact]
    public void PreservesUnicodeNestedPathsAndEmptyFoldersInTree()
    {
        var root = Path.Combine(Path.GetTempPath(), "workspace");
        var path = WorkspaceMutationPolicy.Resolve(root, "café/☕.markdown");
        var tree = WorkspaceTreeBuilder.Build(root, [path], [Path.Combine(root, "empty")]);
        Assert.Equal("café", tree[0].Name);
        Assert.Equal("☕.markdown", Assert.Single(tree[0].Children).Name);
        Assert.Equal("empty", tree[1].Name);
        Assert.Empty(tree[1].Children);
    }
}
