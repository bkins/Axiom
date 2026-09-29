using Axiom.Infrastructure;
using Xunit;

namespace Axiom.Infrastructure.Tests;

public sealed class ExternalFileMonitorTests
{
    [Fact]
    public async Task WatchesFilesInSeveralDirectoriesAndRemovingATabStopsItsWatcher()
    {
        using var fixture = new MutationTestWorkspace();
        Directory.CreateDirectory(Path.Combine(fixture.Root, "nested"));
        var first = Path.Combine(fixture.Root, "first.md");
        var second = Path.Combine(fixture.Root, "nested", "second.md");
        await File.WriteAllTextAsync(first, "first");
        await File.WriteAllTextAsync(second, "second");
        using var monitor = new ExternalFileMonitor();
        var firstChanged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondChanged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.Changed += (_, path) =>
        {
            if (path.Equals(first, StringComparison.OrdinalIgnoreCase)) firstChanged.TrySetResult();
            if (path.Equals(second, StringComparison.OrdinalIgnoreCase)) secondChanged.TrySetResult();
        };
        monitor.WatchMany([first, second, first]);
        await File.WriteAllTextAsync(first, "changed first");
        await File.WriteAllTextAsync(second, "changed second");
        await Task.WhenAll(firstChanged.Task, secondChanged.Task).WaitAsync(TimeSpan.FromSeconds(5));
        monitor.WatchMany([second]);
        firstChanged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        secondChanged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await File.WriteAllTextAsync(first, "closed tab");
        await File.WriteAllTextAsync(second, "still open");
        await secondChanged.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(firstChanged.Task.IsCompleted);
        monitor.Watch(null);
    }
}
