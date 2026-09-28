namespace Axiom.Core;

public interface IWorkspaceCatalogStore
{
    Task<IReadOnlyList<WorkspaceRecord>> ListWorkspacesAsync(CancellationToken cancellationToken = default);
    Task ForgetWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);
}
