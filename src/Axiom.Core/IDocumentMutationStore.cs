namespace Axiom.Core;

public interface IDocumentMutationStore
{
    Task PrepareMutationAsync(DocumentMutationIntent intent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DocumentMutationIntent>> GetPendingMutationsAsync(CancellationToken cancellationToken = default);
    Task ReconcileMutationAsync(DocumentMutationIntent intent, bool applied, CancellationToken cancellationToken = default);
}
