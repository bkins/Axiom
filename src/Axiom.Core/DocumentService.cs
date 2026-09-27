namespace Axiom.Core;

public sealed class DocumentService(
    IDocumentFileStore files,
    IRecoveryStore recovery,
    IDocumentMetadataStore metadata,
    IAxiomLog? log = null)
{
    private readonly IAxiomLog _log = log ?? new NullAxiomLog();

    public DocumentSession NewDocument() => DocumentSession.New();

    public async Task<DocumentSession> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        var file = await files.ReadAsync(path, cancellationToken);
        _log.Information("document.opened", file.Path);
        return DocumentSession.Open(file);
    }

    public async Task SaveAsync(DocumentSession session, CancellationToken cancellationToken = default)
    {
        if (session.Path is null) throw new InvalidOperationException("A path is required. Use Save As for a new document.");
        if (await CheckExternalChangeAsync(session, cancellationToken) && session.Conflict is not null)
            throw new ExternalDocumentConflictException(session.Path);
        await SaveAsAsync(session, session.Path, cancellationToken);
    }

    public async Task SaveAsAsync(DocumentSession session, string path, CancellationToken cancellationToken = default)
    {
        var file = await files.WriteAsync(path, session.Text, cancellationToken);
        session.MarkSaved(file.Path, file.LastWriteUtc);
        await recovery.DeleteDraftAsync(session.DraftKey, cancellationToken);
        if (session.DocumentId is { } documentId)
            await metadata.RecordIdentityAsync(documentId, file.Path, cancellationToken);
        _log.Information("document.saved", file.Path);
    }

    public async Task ProtectAsync(DocumentSession session, CancellationToken cancellationToken = default)
    {
        if (!session.IsDirty) return;
        await recovery.SaveDraftAsync(new RecoveryDraft(
            session.DraftKey,
            session.Path,
            session.Text,
            DateTimeOffset.UtcNow,
            session.LastKnownWriteUtc,
            session.IsNew), cancellationToken);
    }

    public async Task DiscardAsync(DocumentSession session, CancellationToken cancellationToken = default)
    {
        session.DiscardChanges();
        await recovery.DeleteDraftAsync(session.DraftKey, cancellationToken);
    }

    public Guid Enroll(DocumentSession session)
    {
        if (session.DocumentId is { } existing) return existing;
        var id = Guid.NewGuid();
        session.UpdateText(DocumentIdCodec.Enroll(session.Text, id));
        return id;
    }

    public Guid ReassignIdentity(DocumentSession session)
    {
        var id = Guid.NewGuid();
        session.UpdateText(DocumentIdCodec.Reassign(session.Text, id));
        return id;
    }

    public async Task<bool> CheckExternalChangeAsync(DocumentSession session, CancellationToken cancellationToken = default)
    {
        if (session.Path is null || !await files.ExistsAsync(session.Path, cancellationToken)) return false;
        var external = await files.ReadAsync(session.Path, cancellationToken);
        if (external.LastWriteUtc == session.LastKnownWriteUtc && string.Equals(external.Text, session.SavedText, StringComparison.Ordinal))
            return false;

        if (session.IsDirty)
            session.RecordConflict(external);
        else
            session.Reload(external);
        return true;
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> FindDuplicateIdentitiesAsync(
        string folder,
        CancellationToken cancellationToken = default)
    {
        var occurrences = new Dictionary<Guid, List<string>>();
        foreach (var path in (await files.ListMarkdownFilesAsync(folder, cancellationToken)).Files)
        {
            var file = await files.ReadAsync(path, cancellationToken);
            if (DocumentIdCodec.Find(file.Text) is not { } id) continue;
            if (!occurrences.TryGetValue(id, out var paths)) occurrences[id] = paths = [];
            paths.Add(path);
            await metadata.RecordIdentityAsync(id, path, cancellationToken);
        }

        return occurrences
            .Where(pair => pair.Value.Count > 1)
            .ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value);
    }
}

public sealed class ExternalDocumentConflictException(string path)
    : IOException($"The file changed outside Axiom. Resolve both versions before saving: {path}");
