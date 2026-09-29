namespace Axiom.Core;

public interface IWorkspaceLinkIndexStore
{
    Task SaveLinkIndexAsync(Guid workspaceId, WorkspaceBacklinkIndex index, CancellationToken cancellationToken = default);
    Task<WorkspaceBacklinkIndex?> GetLinkIndexAsync(Guid workspaceId, CancellationToken cancellationToken = default);
}
