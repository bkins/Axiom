namespace Axiom.Core;

public interface IDocumentFileStore
{
    Task<MarkdownDiscoveryResult> ListMarkdownFilesAsync(string folder, CancellationToken cancellationToken = default);
    Task<DocumentFile> ReadAsync(string path, CancellationToken cancellationToken = default);
    Task<DocumentFile> WriteAsync(string path, string text, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default);
}

public interface IRecoveryStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<string> BeginSessionAsync(CancellationToken cancellationToken = default);
    Task EndSessionAsync(string sessionId, bool cleanExit, CancellationToken cancellationToken = default);
    Task SaveDraftAsync(RecoveryDraft draft, CancellationToken cancellationToken = default);
    Task DeleteDraftAsync(string draftKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RecoveryDraft>> GetRecoverableDraftsAsync(CancellationToken cancellationToken = default);
}

public interface IDocumentMetadataStore
{
    Task RecordIdentityAsync(Guid documentId, string path, CancellationToken cancellationToken = default);
    Task RemovePathAsync(string path, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DocumentIdentityOccurrence>> GetIdentitiesAsync(CancellationToken cancellationToken = default);
}

public interface IWorkspaceStore
{
    Task<WorkspaceRecord> OpenWorkspaceAsync(
        string normalizedRootPath,
        string displayName,
        DateTimeOffset openedUtc,
        CancellationToken cancellationToken = default);
    Task<WorkspaceRecord?> GetActiveWorkspaceAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkspacePin>> GetPinsAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task AddPinAsync(Guid workspaceId, string relativePath, DateTimeOffset pinnedUtc, CancellationToken cancellationToken = default);
    Task RemovePinAsync(Guid workspaceId, string relativePath, CancellationToken cancellationToken = default);
}

public interface IAxiomLog
{
    void Information(string eventName, string? path = null);
    void Error(string eventName, Exception exception, string? path = null);
}

public sealed class NullAxiomLog : IAxiomLog
{
    public void Information(string eventName, string? path = null) { }
    public void Error(string eventName, Exception exception, string? path = null) { }
}

