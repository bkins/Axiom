using Axiom.Core;

namespace Axiom.App.Tests;

internal sealed class FaultingWorkspaceSessionStore : IWorkspaceSessionStore
{
    public Task SaveWorkspaceSessionAsync(Guid workspaceId, WorkspaceDocumentSession session, CancellationToken cancellationToken = default) =>
        throw new IOException("Injected session persistence failure.");

    public Task<WorkspaceDocumentSession?> GetWorkspaceSessionAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        Task.FromResult<WorkspaceDocumentSession?>(null);
}
