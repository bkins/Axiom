using Axiom.Core;

namespace Axiom.App.Tests;

internal sealed class FailingRecoveryStore(IRecoveryStore inner) : IRecoveryStore
{
    public Task InitializeAsync(CancellationToken cancellationToken = default) => inner.InitializeAsync(cancellationToken);
    public Task<string> BeginSessionAsync(CancellationToken cancellationToken = default) => inner.BeginSessionAsync(cancellationToken);
    public Task EndSessionAsync(string sessionId, bool cleanExit, CancellationToken cancellationToken = default) => inner.EndSessionAsync(sessionId, cleanExit, cancellationToken);
    public Task SaveDraftAsync(RecoveryDraft draft, CancellationToken cancellationToken = default) => throw new IOException("Injected recovery write failure");
    public Task DeleteDraftAsync(string draftKey, CancellationToken cancellationToken = default) => inner.DeleteDraftAsync(draftKey, cancellationToken);
    public Task<IReadOnlyList<RecoveryDraft>> GetRecoverableDraftsAsync(CancellationToken cancellationToken = default) => inner.GetRecoverableDraftsAsync(cancellationToken);
}
