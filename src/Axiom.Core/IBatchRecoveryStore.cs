namespace Axiom.Core;

public interface IBatchRecoveryStore
{
    Task ApplyDraftsAsync(
        IReadOnlyList<RecoveryDraft> drafts,
        IReadOnlyList<string> deletedKeys,
        CancellationToken cancellationToken = default);
}
