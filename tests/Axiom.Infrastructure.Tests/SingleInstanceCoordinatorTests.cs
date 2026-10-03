using Axiom.Infrastructure;

namespace Axiom.Infrastructure.Tests;

public sealed class SingleInstanceCoordinatorTests
{
    [Fact]
    public void SecondaryThreadForwardsOnlyMarkdownToTheStateRootOwner()
    {
        var root = Path.Combine(Path.GetTempPath(), "Axiom-owner-" + Guid.NewGuid().ToString("N"));
        using var owner = new SingleInstanceCoordinator(root);
        Assert.True(owner.IsPrimary);
        string? received = null;
        owner.Listen(path => received = path);
        var expected = Path.Combine(root, "example.markdown");
        Exception? failure = null;
        var client = new Thread(() =>
        {
            try
            {
                using var secondary = new SingleInstanceCoordinator(root + Path.DirectorySeparatorChar);
                Assert.False(secondary.IsPrimary);
                Assert.True(secondary.ForwardAsync(expected).GetAwaiter().GetResult());
            }
            catch (Exception exception) { failure = exception; }
        });
        client.Start();
        Assert.True(client.Join(TimeSpan.FromSeconds(12)));
        Assert.Null(failure);
        Assert.Equal(expected, received);
        using var independent = new SingleInstanceCoordinator(root + "-isolated");
        Assert.True(independent.IsPrimary);
    }

    [Fact]
    public void RejectedNonMarkdownRequestDoesNotActivateOwnerAndNextRequestStillWorks()
    {
        var root = Path.Combine(Path.GetTempPath(), "Axiom-owner-" + Guid.NewGuid().ToString("N"));
        using var owner = new SingleInstanceCoordinator(root);
        var requests = 0;
        owner.Listen(_ => Interlocked.Increment(ref requests));
        Exception? failure = null;
        var client = new Thread(() =>
        {
            try
            {
                using var secondary = new SingleInstanceCoordinator(root);
                Assert.False(secondary.ForwardAsync(Path.Combine(root, "file.exe")).GetAwaiter().GetResult());
                Assert.True(secondary.ForwardAsync(null).GetAwaiter().GetResult());
            }
            catch (Exception exception) { failure = exception; }
        });
        client.Start();
        Assert.True(client.Join(TimeSpan.FromSeconds(12)));
        Assert.Null(failure);
        Assert.Equal(1, requests);
    }
}
