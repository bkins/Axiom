using Axiom.Core;

namespace Axiom.Infrastructure.Tests;

public sealed class SemanticResultTests
{
    [Fact]
    public async Task CachePersistsOnlyMatchingContentRevisionAndProviderProvenance()
    {
        using var fixture = new MutationTestWorkspace();
        var snapshot = SemanticDocumentSnapshot.Create("workspace/document", 3, new MarkdownStructureService().Parse("# Original\n"));
        var result = new SemanticDocumentResult("provider", "1", snapshot.DocumentKey, snapshot.ContentHash, snapshot.Revision, DateTimeOffset.UtcNow, "{\"summary\":\"derived only\"}");
        await fixture.State.WriteAsync(result);
        var restarted = new SqliteStateStore(fixture.State.Paths);
        await restarted.InitializeAsync();
        Assert.Equal(result, await restarted.ReadAsync(snapshot.DocumentKey, "provider", "1", snapshot.ContentHash, 3));
        Assert.Null(await restarted.ReadAsync(snapshot.DocumentKey, "provider", "2", snapshot.ContentHash, 3));
        Assert.Null(await restarted.ReadAsync(snapshot.DocumentKey, "provider", "1", snapshot.ContentHash, 4));
        Assert.False(result.AppliesTo(SemanticDocumentSnapshot.Create(snapshot.DocumentKey, 4, new MarkdownStructureService().Parse("# Edited")), "provider", "1"));
        await Assert.ThrowsAsync<ArgumentException>(() => restarted.WriteAsync(result with { ContentHash = "invalid" }));
        Assert.Empty(await restarted.GetIdentitiesAsync());
        Assert.Empty(await restarted.GetClassificationsAsync());
    }
}
