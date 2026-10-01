using Axiom.Core;
using Axiom.Infrastructure;
using Xunit;

namespace Axiom.Infrastructure.Tests;

public sealed class WorkspaceLinkIndexTests
{
    [Fact]
    public async Task WikiAliasesAndIdentityProducePersistedBacklinksWithoutChangingSource()
    {
        using var fixture = new MutationTestWorkspace();
        var identity = Guid.NewGuid();
        var source = Path.Combine(fixture.Root, "source.md");
        var target = Path.Combine(fixture.Root, "target.md");
        var text = $"[[Alternate|Go]] and [[id:{identity:D}#Heading]]\n";
        await File.WriteAllTextAsync(source, text);
        await File.WriteAllTextAsync(target, $"<!-- axiom:documentId={identity:D} -->\n# Heading\n");
        await fixture.State.ApplyClassificationsAsync([new(new(identity, [], null, ["Alternate"]), 0)]);
        var files = new FileDocumentStore();
        var service = new WorkspaceLinkIndexService(files, new MarkdownLinkTargetReader(files), fixture.State);
        var index = await service.BuildAsync(fixture.Root);
        await fixture.State.SaveLinkIndexAsync(fixture.Workspace.WorkspaceId, index);
        var restored = (await fixture.State.GetLinkIndexAsync(fixture.Workspace.WorkspaceId))!;
        var incoming = WorkspaceLinkIndexService.Incoming("target.md", restored, new Dictionary<string, IReadOnlyList<WorkspaceLinkReference>>());
        Assert.Equal(2, incoming.Count);
        Assert.All(incoming, reference => Assert.Equal(MarkdownLinkResolutionStatus.Resolved, reference.Status));
        Assert.Equal(text, await File.ReadAllTextAsync(source));
        Assert.Null(DocumentIdCodec.Find(await File.ReadAllTextAsync(source)));
    }

    [Fact]
    public async Task FailedSourceReadRetainsPreviousReferencesAsPartialWhileDeletionRemovesThem()
    {
        using var fixture = new MutationTestWorkspace();
        var source = Path.Combine(fixture.Root, "source.md");
        await File.WriteAllTextAsync(source, "[Go](target.md)");
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "target.md"), "target");
        var files = new FileDocumentStore();
        var service = new WorkspaceLinkIndexService(files, new MarkdownLinkTargetReader(files));
        var original = await service.BuildAsync(fixture.Root);
        await using (var locked = new FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var partial = await service.BuildAsync(fixture.Root, previous: original);
            Assert.NotEmpty(partial.Errors);
            Assert.Equal(original.Documents.SelectMany(document => document.References), partial.Documents.SelectMany(document => document.References));
        }
        File.Delete(source);
        var deleted = await service.BuildAsync(fixture.Root, previous: original);
        Assert.Empty(deleted.Documents.SelectMany(document => document.References));
    }

    [Fact]
    public async Task SavedIndexRebuildAndPersistenceAreEquivalentWithoutEnrollmentOrMarkdownWrites()
    {
        using var fixture = new MutationTestWorkspace();
        var source = Path.Combine(fixture.Root, "source.md");
        var target = Path.Combine(fixture.Root, "target name.md");
        const string text = "# Source context\n\n[Go][target]\n\n[target]: target%20name.md#heading\n\n![Image](target%20name.md)\n\n`[Example](target%20name.md)`\n";
        await File.WriteAllTextAsync(source, text);
        await File.WriteAllTextAsync(target, "# Heading\n");
        var files = new FileDocumentStore();
        var service = new WorkspaceLinkIndexService(files, new MarkdownLinkTargetReader(files));
        var first = await service.BuildAsync(fixture.Root);
        var rebuilt = await service.BuildAsync(fixture.Root);
        Assert.Equal(first.Documents.SelectMany(document => document.References), rebuilt.Documents.SelectMany(document => document.References));
        await fixture.State.SaveLinkIndexAsync(fixture.Workspace.WorkspaceId, first);
        var reopened = new SqliteStateStore(fixture.State.Paths);
        var restored = (await reopened.GetLinkIndexAsync(fixture.Workspace.WorkspaceId))!;
        var incoming = Assert.Single(WorkspaceLinkIndexService.Incoming("target name.md", restored, new Dictionary<string, IReadOnlyList<WorkspaceLinkReference>>()));
        Assert.Equal("source.md", incoming.SourceRelativePath);
        Assert.Contains("Source context", incoming.Context);
        Assert.Equal(3, incoming.Source.LineNumber);
        Assert.Equal(MarkdownLinkResolutionStatus.Resolved, incoming.Status);
        Assert.Equal(text, await File.ReadAllTextAsync(source));
        Assert.Empty(await fixture.State.GetIdentitiesAsync());
        await fixture.State.ForgetWorkspaceAsync(fixture.Workspace.WorkspaceId);
        Assert.Null(await fixture.State.GetLinkIndexAsync(fixture.Workspace.WorkspaceId));
    }

    [Fact]
    public async Task MissingHeadingsTargetsAndUnsupportedLinksHaveExplicitStatesAndCancellationLeavesStoredIndex()
    {
        using var fixture = new MutationTestWorkspace();
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "source.md"), "[Missing](missing.md)\n[Anchor](target.md#missing)\n[External](https://example.com)\n");
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "target.md"), "# Present\n");
        var files = new FileDocumentStore();
        var service = new WorkspaceLinkIndexService(files, new MarkdownLinkTargetReader(files));
        var index = await service.BuildAsync(fixture.Root);
        var statuses = index.Documents.SelectMany(document => document.References).Select(reference => reference.Status).ToArray();
        Assert.Contains(MarkdownLinkResolutionStatus.MissingDocument, statuses);
        Assert.Contains(MarkdownLinkResolutionStatus.MissingHeading, statuses);
        Assert.Contains(MarkdownLinkResolutionStatus.UnsupportedScheme, statuses);
        await fixture.State.SaveLinkIndexAsync(fixture.Workspace.WorkspaceId, index);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.BuildAsync(fixture.Root, cancellationToken: cancellation.Token));
        Assert.Equal(index.BuiltUtc, (await fixture.State.GetLinkIndexAsync(fixture.Workspace.WorkspaceId))!.BuiltUtc);
    }
}
