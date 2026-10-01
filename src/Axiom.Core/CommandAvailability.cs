namespace Axiom.Core;

public sealed record CommandAvailability(bool Enabled, string? Reason = null)
{
    public static CommandAvailability Available { get; } = new(true);
}
