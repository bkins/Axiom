namespace Axiom.Core;

public interface IWorkspaceSessionStore
{
    Task SaveWorkspaceSessionAsync(Guid workspaceId, WorkspaceDocumentSession session, CancellationToken cancellationToken = default);
    Task<WorkspaceDocumentSession?> GetWorkspaceSessionAsync(Guid workspaceId, CancellationToken cancellationToken = default);
}
