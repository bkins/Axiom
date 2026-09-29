using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App.Tests;

internal sealed class DelayedBatchRecoveryStore(SqliteStateStore inner) : IRecoveryStore, IBatchRecoveryStore
{
    private bool _delayFirstWrite = true;
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task ApplyDraftsAsync(IReadOnlyList<RecoveryDraft> drafts, IReadOnlyList<string> deletedKeys, CancellationToken cancellationToken = default)
    {
        if (_delayFirstWrite)
        {
            _delayFirstWrite = false;
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
        await inner.ApplyDraftsAsync(drafts, deletedKeys, cancellationToken);
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) => inner.InitializeAsync(cancellationToken);
    public Task<string> BeginSessionAsync(CancellationToken cancellationToken = default) => inner.BeginSessionAsync(cancellationToken);
    public Task EndSessionAsync(string sessionId, bool cleanExit, CancellationToken cancellationToken = default) => inner.EndSessionAsync(sessionId, cleanExit, cancellationToken);
    public Task SaveDraftAsync(RecoveryDraft draft, CancellationToken cancellationToken = default) => inner.SaveDraftAsync(draft, cancellationToken);
    public Task DeleteDraftAsync(string draftKey, CancellationToken cancellationToken = default) => inner.DeleteDraftAsync(draftKey, cancellationToken);
    public Task<IReadOnlyList<RecoveryDraft>> GetRecoverableDraftsAsync(CancellationToken cancellationToken = default) => inner.GetRecoverableDraftsAsync(cancellationToken);
}
