using Axiom.Core;
using Xunit;

namespace Axiom.Core.Tests;

public sealed class WorkspaceDocumentResolverTests
{
    [Fact]
    public void ActiveContainingWorkspaceWinsAndOtherwiseMostSpecificRootWins()
    {
        var root = Path.Combine(Path.GetTempPath(), "Axiom-resolver");
        var parent = Workspace(root);
        var nested = Workspace(Path.Combine(root, "nested"));
        var file = Path.Combine(nested.RootPath, "note.md");
        Assert.Same(parent, WorkspaceDocumentResolver.Resolve(file, [nested, parent], parent.RootPath));
        Assert.Same(nested, WorkspaceDocumentResolver.Resolve(file, [parent, nested], null));
        Assert.Same(nested, WorkspaceDocumentResolver.Resolve(file, [parent, nested], root + "-other"));
    }

    [Fact]
    public void SiblingPrefixAndStandaloneFilesDoNotInventMembership()
    {
        var root = Path.Combine(Path.GetTempPath(), "Axiom-resolver");
        var workspace = Workspace(root);
        Assert.Null(WorkspaceDocumentResolver.Resolve(Path.Combine(root + "-other", "note.md"), [workspace], root));
        Assert.Null(WorkspaceDocumentResolver.Resolve(root, [workspace], null));
        Assert.Null(WorkspaceDocumentResolver.Resolve(Path.Combine(root, "note.md"), [], null));
    }

    [Fact]
    public void EquivalentRootsUseRecentOpeningAndStableIdentityForTies()
    {
        var root = Path.Combine(Path.GetTempPath(), "Axiom-resolver");
        var older = Workspace(root) with { LastOpenedUtc = DateTimeOffset.MinValue };
        var recent = Workspace(root);
        var file = Path.Combine(root, "note.md");
        Assert.Same(recent, WorkspaceDocumentResolver.Resolve(file, [older, recent], null));
        var stable = recent with { WorkspaceId = Guid.Empty };
        Assert.Same(stable, WorkspaceDocumentResolver.Resolve(file, [recent, stable], null));
    }

    private static WorkspaceRecord Workspace(string root) =>
        new(Guid.NewGuid(), root, "Fixture", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
}
