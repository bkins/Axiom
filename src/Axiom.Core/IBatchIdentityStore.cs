namespace Axiom.Core;

public interface IBatchIdentityStore
{
    Task RecordIdentitiesAsync(IReadOnlyList<DocumentIdentityOccurrence> identities, CancellationToken cancellationToken = default);
}
