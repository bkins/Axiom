namespace Axiom.Infrastructure;

internal sealed record GitCommandResult(int ExitCode, byte[] Output, string Error);
