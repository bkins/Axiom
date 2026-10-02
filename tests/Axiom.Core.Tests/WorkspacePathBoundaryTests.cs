namespace Axiom.Core.Tests;

public sealed class WorkspacePathBoundaryTests
{
    [Fact]
    public void ContainsNestedUnicodePathsButRejectsSiblingPrefixesAndNormalizedEscapes()
    {
        var root = Path.Combine(Path.GetTempPath(), "BoundaryWorkspace");
        Assert.True(WorkspacePathBoundary.Contains(root, Path.Combine(root, "café", "☕.md")));
        Assert.False(WorkspacePathBoundary.Contains(root, Path.Combine(root + "-other", "a.md")));
        Assert.False(WorkspacePathBoundary.Contains(root, Path.Combine(root, "..", "a.md")));
        Assert.False(WorkspacePathBoundary.Contains(root, root));
        Assert.True(WorkspacePathBoundary.Contains(root, root, includeRoot: true));
        Assert.True(WorkspacePathBoundary.Contains(root + Path.DirectorySeparatorChar, Path.Combine(root, "a.md")));
        var volumeRoot = Path.GetPathRoot(Path.GetFullPath(root))!;
        Assert.False(WorkspacePathBoundary.Contains(volumeRoot, volumeRoot));
        Assert.True(WorkspacePathBoundary.Contains(volumeRoot, volumeRoot, includeRoot: true));
    }

    [Fact]
    public void RootCaseAliasesAreAllowedOnlyOnWindows()
    {
        var root = Path.Combine(Path.GetTempPath(), "BoundaryWorkspace");
        var alias = Path.Combine(Path.GetTempPath(), "boundaryworkspace", "a.md");
        Assert.Equal(OperatingSystem.IsWindows(), WorkspacePathBoundary.Contains(root, alias));
        if (!OperatingSystem.IsWindows())
            Assert.Throws<ArgumentException>(() => WorkspaceMutationPolicy.MoveDestination(root, "a.md", Path.GetDirectoryName(alias)!));
    }
}
