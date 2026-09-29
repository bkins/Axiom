using Axiom.Core;
using Xunit;

namespace Axiom.Infrastructure.Tests;

public sealed class IdentityBatchTests
{
    [Fact]
    public async Task CancellationCannotPublishPartialIdentityDiscoveryAndRepeatedObservationsStayDistinct()
    {
        using var fixture = new MutationTestWorkspace();
        var observations = Enumerable.Range(0, 10000).Select(index => new DocumentIdentityOccurrence(Guid.NewGuid(), Path.Combine(fixture.Root, $"{index}.md"))).ToArray();
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(5);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.State.RecordIdentitiesAsync(observations, cancellation.Token));
        Assert.Empty(await fixture.State.GetIdentitiesAsync());
        var id = Guid.NewGuid();
        var small = new[] { new DocumentIdentityOccurrence(id, Path.Combine(fixture.Root, "one.md")), new DocumentIdentityOccurrence(id, Path.Combine(fixture.Root, "two.md")) };
        await fixture.State.RecordIdentitiesAsync(small);
        await fixture.State.RecordIdentitiesAsync(small);
        Assert.Equal(2, (await fixture.State.GetIdentitiesAsync()).Count);
    }
}
