namespace Axiom.Core;

public sealed record CommandResult(CommandOutcome Outcome, string Message, Exception? Error = null);
