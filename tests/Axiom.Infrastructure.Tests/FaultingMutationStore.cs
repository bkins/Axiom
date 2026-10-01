using Axiom.Core;

namespace Axiom.Infrastructure.Tests;

internal sealed class FaultingMutationStore(IDocumentMutationStore inner) : IDocumentMutationStore, IDeletedDocumentStore
{
    public Action<DocumentMutationIntent>? AfterPrepare { get; set; }
    public bool FailReconciliation { get; set; }
    public bool FailCapture { get; set; }

    public async Task PrepareDeletionAsync(DocumentMutationIntent intent, byte[] content, CancellationToken cancellationToken = default)
    {
        if (FailCapture) throw new IOException("Injected capture failure.");
        await ((IDeletedDocumentStore)inner).PrepareDeletionAsync(intent, content, cancellationToken);
        AfterPrepare?.Invoke(intent);
    }

    public Task<IReadOnlyList<DeletedDocument>> GetDeletedDocumentsAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        ((IDeletedDocumentStore)inner).GetDeletedDocumentsAsync(workspaceId, cancellationToken);

    public async Task PrepareRestoreAsync(DocumentMutationIntent intent, CancellationToken cancellationToken = default)
    {
        await ((IDeletedDocumentStore)inner).PrepareRestoreAsync(intent, cancellationToken);
        AfterPrepare?.Invoke(intent);
    }

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
