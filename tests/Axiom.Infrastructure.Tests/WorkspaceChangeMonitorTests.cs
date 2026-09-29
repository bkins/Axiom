using Axiom.Infrastructure;
using Xunit;

namespace Axiom.Infrastructure.Tests;

public sealed class WorkspaceChangeMonitorTests
{
    [Fact]
    public async Task ObservesRestoredNestedFileAndStopsObservingThePreviousRoot()
    {
        using var fixture = new MutationTestWorkspace();
        var first = Path.Combine(fixture.Root, "first");
        var second = Path.Combine(fixture.Root, "second");
        var nested = Path.Combine(first, "nested");
        Directory.CreateDirectory(nested);
        Directory.CreateDirectory(second);
        using var monitor = new WorkspaceChangeMonitor();
        var changed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.Changed += (_, root) => changed.TrySetResult(root);
        monitor.Watch(first);
        await File.WriteAllTextAsync(Path.Combine(nested, "restored.md"), "restored bytes");
        Assert.Equal(first, await changed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        monitor.Watch(second);
        changed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await File.WriteAllTextAsync(Path.Combine(nested, "old-root.md"), "not active");
        await File.WriteAllTextAsync(Path.Combine(second, "active.md"), "active");
        Assert.Equal(second, await changed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        monitor.Watch(null);
        changed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await File.WriteAllTextAsync(Path.Combine(second, "ignored.md"), "ignored");
        await Task.Delay(100);
        Assert.False(changed.Task.IsCompleted);
    }
}
