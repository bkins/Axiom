using Axiom.Core;

namespace Axiom.Core.Tests;

public sealed class CommandDispatcherTests
{
    private static CommandContext Context => new("draft", null, null, false, true, true, false, false, true);

    [Fact]
    public async Task AvailabilityAuthorizationAndCancellationPreventExecution()
    {
        var dispatcher = new CommandDispatcher();
        var count = 0;
        var command = new AxiomCommand("save", "Save", "Ctrl+S", true, _ => CommandAvailability.Available, (_, _) =>
        {
            count++;
            return Task.FromResult(new CommandResult(CommandOutcome.Completed, "Saved"));
        });
        dispatcher.Register(command);
        Assert.Throws<ArgumentException>(() => dispatcher.Register(command));
        Assert.Equal(CommandOutcome.Unavailable, (await dispatcher.ExecuteAsync("unknown", Context)).Outcome);
        Assert.Equal(CommandOutcome.Unavailable, (await dispatcher.ExecuteAsync("save", Context with { IsBusy = true })).Outcome);
        Assert.Equal(CommandOutcome.Unavailable, (await dispatcher.ExecuteAsync("save", Context with { UserInitiated = false })).Outcome);
        Assert.Equal(CommandOutcome.Unavailable, (await dispatcher.ExecuteAsync("save", Context with { MutationBlocked = true })).Outcome);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Equal(CommandOutcome.Cancelled, (await dispatcher.ExecuteAsync("save", Context, cancellation.Token)).Outcome);
        Assert.Equal(0, count);
        Assert.Equal(CommandOutcome.Completed, (await dispatcher.ExecuteAsync("save", Context)).Outcome);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task ConcurrentCommandsAndFailuresReleaseGateWithoutMislabelingCommittedCancellation()
    {
        var dispatcher = new CommandDispatcher();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.Register(new("long", "Long", null, false, _ => CommandAvailability.Available, async (_, _) =>
        {
            started.SetResult();
            await finish.Task;
            return new(CommandOutcome.Completed, "Done");
        }));
        dispatcher.Register(new("fail", "Fail", null, false, _ => CommandAvailability.Available, (_, _) => throw new IOException("Fixture failure")));
        var running = dispatcher.ExecuteAsync("long", Context);
        await started.Task;
        Assert.Equal(CommandOutcome.Unavailable, (await dispatcher.ExecuteAsync("fail", Context)).Outcome);
        finish.SetResult();
        Assert.Equal(CommandOutcome.Completed, (await running).Outcome);
        var failure = await dispatcher.ExecuteAsync("fail", Context);
        Assert.Equal(CommandOutcome.Failed, failure.Outcome);
        Assert.IsType<IOException>(failure.Error);
        using var cancellation = new CancellationTokenSource();
        dispatcher.Register(new("commit", "Commit", null, true, _ => CommandAvailability.Available, (_, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult(new CommandResult(CommandOutcome.Completed, "Committed"));
        }));
        Assert.Equal(CommandOutcome.Completed, (await dispatcher.ExecuteAsync("commit", Context, cancellation.Token)).Outcome);
    }
}
