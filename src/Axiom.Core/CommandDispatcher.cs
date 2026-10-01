namespace Axiom.Core;

public sealed class CommandDispatcher
{
    private readonly Dictionary<string, AxiomCommand> _commands = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _executionGate = new(1, 1);

    public IReadOnlyList<AxiomCommand> Commands => _commands.Values.OrderBy(command => command.Title, StringComparer.OrdinalIgnoreCase).ToArray();

    public void Register(AxiomCommand command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Title);
        if (command.IsCancellationControl && command.MutatesFiles)
            throw new ArgumentException("Cancellation controls cannot perform file mutations.");
        if (!_commands.TryAdd(command.Id, command)) throw new ArgumentException("A command with this ID is already registered.");
    }

    public CommandAvailability GetAvailability(string id, CommandContext context)
    {
        if (!_commands.TryGetValue(id, out var command)) return new(false, "Unknown command.");
        if (context.IsBusy && !command.IsCancellationControl) return new(false, "Wait for the current document operation to finish.");
        if (command.IsCancellationControl && !context.UserInitiated) return new(false, "Cancellation requires a deliberate user invocation.");
        if (command.MutatesFiles && !context.UserInitiated) return new(false, "File mutations require a deliberate user invocation.");
        if (command.MutatesFiles && context.MutationBlocked) return new(false, "Restart and reconcile pending file operations first.");
        return command.Availability(context);
    }

    public async Task<CommandResult> ExecuteAsync(string id, CommandContext context, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return new(CommandOutcome.Cancelled, "Command cancelled before execution.");
        var availability = GetAvailability(id, context);
        if (!availability.Enabled) return new(CommandOutcome.Unavailable, availability.Reason ?? "Command unavailable.");
        var acquired = false;
        try
        {
            if (!_commands[id].IsCancellationControl)
            {
                acquired = await _executionGate.WaitAsync(0, cancellationToken);
                if (!acquired) return new(CommandOutcome.Unavailable, "Another command is running.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(CommandOutcome.Cancelled, "Command cancelled before execution.");
        }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Each action owns its confirmations and commit boundary. Cancellation
            // after a successful commit must not relabel that result as cancelled.
            return await _commands[id].Execute(context, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(CommandOutcome.Cancelled, "Command cancelled.");
        }
        catch (Exception exception) { return new(CommandOutcome.Failed, exception.Message, exception); }
        finally { if (acquired) _executionGate.Release(); }
    }
}
