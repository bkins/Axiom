namespace Axiom.Core;

public interface IReadingModeStore
{
    Task<bool> GetReadingModeAsync(CancellationToken cancellationToken = default);
    Task SetReadingModeAsync(bool enabled, CancellationToken cancellationToken = default);
}
