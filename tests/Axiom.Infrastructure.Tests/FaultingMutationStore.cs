using Axiom.Core;

namespace Axiom.Infrastructure.Tests;

internal sealed class FaultingMutationStore(IDocumentMutationStore inner) : IDocumentMutationStore
{
    public Action<DocumentMutationIntent>? AfterPrepare { get; set; }
    public bool FailReconciliation { get; set; }

    public async Task PrepareMutationAsync(DocumentMutationIntent intent, CancellationToken cancellationToken = default)
    {
        await inner.PrepareMutationAsync(intent, cancellationToken);
        AfterPrepare?.Invoke(intent);
    }

    public Task<IReadOnlyList<DocumentMutationIntent>> GetPendingMutationsAsync(CancellationToken cancellationToken = default) => inner.GetPendingMutationsAsync(cancellationToken);

    public Task ReconcileMutationAsync(DocumentMutationIntent intent, bool applied, CancellationToken cancellationToken = default)
    {
        if (FailReconciliation) throw new IOException("Injected state failure.");
        return inner.ReconcileMutationAsync(intent, applied, cancellationToken);
    }
}
