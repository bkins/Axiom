using Axiom.Core;

namespace Axiom.Core.Tests;

public sealed class DocumentServiceTests
{
    [Fact]
    public async Task ExplicitNewIdentityHasAnEditableBodyAndSurvivesRecoverySaveAndReopen()
    {
        var files = new MemoryFiles("C:\\docs\\new.md", "");
        var recovery = new MemoryRecovery();
        var service = new DocumentService(files, recovery, new MemoryMetadata());
        var session = service.NewAxiomDocument();
        var identity = Assert.IsType<Guid>(session.DocumentId);
        Assert.EndsWith(Environment.NewLine + Environment.NewLine, session.Text);
        session.UpdateText(session.Text + "# Body");
        await service.ProtectAsync(session);
        var restored = DocumentSession.Recover(Assert.Single(recovery.Drafts));
        Assert.Equal(identity, restored.DocumentId);
        await service.SaveAsAsync(restored, "C:\\docs\\new.md");
        Assert.Empty(recovery.Drafts);
        var reopened = await service.OpenAsync("C:\\docs\\new.md");
        Assert.Equal(identity, reopened.DocumentId);
        Assert.Equal(restored.Text, reopened.Text);
        Assert.False(reopened.IsDirty);
    }

    [Fact]
    public async Task OpeningAnExistingFileDoesNotChangeOrEnrollIt()
    {
        var files = new MemoryFiles("C:\\docs\\plain.md", "# Plain");
        var recovery = new MemoryRecovery();
        var service = new DocumentService(files, recovery, new MemoryMetadata());

        var session = await service.OpenAsync("C:\\docs\\plain.md");

        Assert.Equal("# Plain", session.Text);
        Assert.Null(session.DocumentId);
        Assert.Equal(0, files.WriteCount);
    }

    [Fact]
    public async Task DirtyTextIsProtectedWithoutWritingMarkdownAndSaveClearsDraft()
    {
        var files = new MemoryFiles("C:\\docs\\one.md", "saved");
        var recovery = new MemoryRecovery();
        var service = new DocumentService(files, recovery, new MemoryMetadata());
        var session = await service.OpenAsync("C:\\docs\\one.md");
        session.UpdateText("working");

        await service.ProtectAsync(session);

        Assert.Equal("working", recovery.Drafts.Single().Text);
        Assert.Equal(0, files.WriteCount);
        await service.SaveAsync(session);
        Assert.Equal("working", files.Text);
        Assert.Empty(recovery.Drafts);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public async Task ReturningToSavedTextClearsTheProtectedDraft()
    {
        var files = new MemoryFiles("C:\\docs\\one.md", "saved");
        var recovery = new MemoryRecovery();
        var service = new DocumentService(files, recovery, new MemoryMetadata());
        var session = await service.OpenAsync("C:\\docs\\one.md");
        session.UpdateText("temporary edit");
        await service.ProtectAsync(session);

        session.UpdateText("saved");
        await service.ProtectAsync(session);

        Assert.False(session.IsDirty);
        Assert.Empty(recovery.Drafts);
    }

    [Fact]
    public async Task ExternalEditCreatesConflictWhenLocalBufferIsDirty()
    {
        var files = new MemoryFiles("C:\\docs\\one.md", "saved");
        var service = new DocumentService(files, new MemoryRecovery(), new MemoryMetadata());
        var session = await service.OpenAsync("C:\\docs\\one.md");
        session.UpdateText("mine");
        files.ChangeExternally("theirs");

        Assert.True(await service.CheckExternalChangeAsync(session));
        Assert.Equal("mine", session.Conflict?.LocalText);
        Assert.Equal("theirs", session.Conflict?.ExternalText);
        Assert.Equal("mine", session.Text);
    }

    [Fact]
    public async Task SaveRefusesToOverwriteAnUnresolvedExternalEdit()
    {
        var files = new MemoryFiles("C:\\docs\\one.md", "saved");
        var service = new DocumentService(files, new MemoryRecovery(), new MemoryMetadata());
        var session = await service.OpenAsync("C:\\docs\\one.md");
        session.UpdateText("mine");
        files.ChangeExternally("theirs");

        await Assert.ThrowsAsync<ExternalDocumentConflictException>(() => service.SaveAsync(session));

        Assert.Equal("theirs", files.Text);
        Assert.Equal(0, files.WriteCount);
        Assert.NotNull(session.Conflict);
    }

    [Fact]
    public async Task ExternalEditReloadsCleanBuffer()
    {
        var files = new MemoryFiles("C:\\docs\\one.md", "saved");
        var service = new DocumentService(files, new MemoryRecovery(), new MemoryMetadata());
        var session = await service.OpenAsync("C:\\docs\\one.md");
        files.ChangeExternally("new disk text");

        await service.CheckExternalChangeAsync(session);

        Assert.Equal("new disk text", session.Text);
        Assert.False(session.IsDirty);
        Assert.Null(session.Conflict);
    }

    [Fact]
    public void EnrollmentIsExplicitStableAndDoesNotCreateSidecarContent()
    {
        var service = new DocumentService(new MemoryFiles(), new MemoryRecovery(), new MemoryMetadata());
        var session = service.NewDocument();
        session.UpdateText("# Title");

        var first = service.Enroll(session);
        var second = service.Enroll(session);

        Assert.Equal(first, second);
        Assert.Contains($"<!-- axiom:documentId={first:D} -->", session.Text);
        Assert.EndsWith("# Title", session.Text);
    }

    [Fact]
    public async Task DuplicateIdsAreReportedByPath()
    {
        var id = Guid.NewGuid();
        var files = new MemoryFiles(new Dictionary<string, string>
        {
            ["C:\\docs\\a.md"] = DocumentIdCodec.Enroll("a", id),
            ["C:\\docs\\copy.md"] = DocumentIdCodec.Enroll("b", id),
            ["C:\\docs\\plain.md"] = "plain"
        });
        var service = new DocumentService(files, new MemoryRecovery(), new MemoryMetadata());

        var duplicates = await service.FindDuplicateIdentitiesAsync("C:\\docs");

        Assert.Equal(2, duplicates[id].Count);
    }

    private sealed class MemoryFiles : IDocumentFileStore
    {
        private readonly Dictionary<string, string> _files;
        private DateTimeOffset _writeUtc = DateTimeOffset.UtcNow;
        public int WriteCount { get; private set; }
        public string Text => _files.Values.Single();

        public MemoryFiles() : this(new Dictionary<string, string>()) { }
        public MemoryFiles(string path, string text) : this(new Dictionary<string, string> { [path] = text }) { }
        public MemoryFiles(Dictionary<string, string> files) => _files = new Dictionary<string, string>(files, StringComparer.OrdinalIgnoreCase);
        public Task<MarkdownDiscoveryResult> ListMarkdownFilesAsync(string folder, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MarkdownDiscoveryResult(_files.Keys.ToArray(), []));
        public Task<DocumentFile> ReadAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(new DocumentFile(path, _files[path], _writeUtc, _files[path].Length));
        public Task<DocumentFile> WriteAsync(string path, string text, CancellationToken cancellationToken = default)
        {
            _files[path] = text; WriteCount++; _writeUtc = _writeUtc.AddSeconds(1);
            return ReadAsync(path, cancellationToken);
        }
        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(_files.ContainsKey(path));
        public void ChangeExternally(string text) { var key = _files.Keys.Single(); _files[key] = text; _writeUtc = _writeUtc.AddSeconds(1); }
    }

    private sealed class MemoryRecovery : IRecoveryStore
    {
        public List<RecoveryDraft> Drafts { get; } = [];
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string> BeginSessionAsync(CancellationToken cancellationToken = default) => Task.FromResult("session");
        public Task EndSessionAsync(string sessionId, bool cleanExit, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveDraftAsync(RecoveryDraft draft, CancellationToken cancellationToken = default) { Drafts.RemoveAll(item => item.DraftKey == draft.DraftKey); Drafts.Add(draft); return Task.CompletedTask; }
        public Task DeleteDraftAsync(string draftKey, CancellationToken cancellationToken = default) { Drafts.RemoveAll(item => item.DraftKey == draftKey); return Task.CompletedTask; }
        public Task<IReadOnlyList<RecoveryDraft>> GetRecoverableDraftsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RecoveryDraft>>(Drafts);
    }

    private sealed class MemoryMetadata : IDocumentMetadataStore
    {
        public Task RecordIdentityAsync(Guid documentId, string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemovePathAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DocumentIdentityOccurrence>> GetIdentitiesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DocumentIdentityOccurrence>>([]);
    }
}
