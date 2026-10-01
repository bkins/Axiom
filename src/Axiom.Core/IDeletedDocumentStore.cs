namespace Axiom.Core;

public interface IDeletedDocumentStore
{
    Task PrepareDeletionAsync(DocumentMutationIntent intent, byte[] content, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeletedDocument>> GetDeletedDocumentsAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task PrepareRestoreAsync(DocumentMutationIntent intent, CancellationToken cancellationToken = default);
}
