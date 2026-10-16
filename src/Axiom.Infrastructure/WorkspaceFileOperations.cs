using System.Security.Cryptography;
using Axiom.Core;

namespace Axiom.Infrastructure;

public sealed partial class WorkspaceFileOperations(IDocumentMutationStore state
                                         , IRecycleBin? recycle = null
                                         , IAxiomLog? log = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IRecycleBin _recycle = recycle ?? (OperatingSystem.IsMacOS() ? new MacRecycleBin()
        : OperatingSystem.IsLinux() ? new LinuxRecycleBin() : new WindowsRecycleBin());
    private readonly IAxiomLog _log = log ?? new NullAxiomLog();

    public async Task<string?> ExecuteAsync(WorkspaceRecord workspace
                                         , DocumentMutationKind kind
                                         , string? sourceRelative
                                         , string? destinationRelative
                                         , string? reviewedHash = null
                                         , CancellationToken cancellationToken = default
                                         , string? creationText = null
                                         , byte[]? importContent = null
                                         , Guid? recoveryOperationId = null
                                         , Guid? createdDocumentId = null)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if ((await state.GetPendingMutationsAsync(cancellationToken)).Count > 0)
                throw new IOException("A previous file operation needs reconciliation. Restart Axiom before making another change.");
            var source = sourceRelative is null ? null : WorkspaceMutationPolicy.Resolve(workspace.RootPath, sourceRelative);
            var destination = destinationRelative is null ? null : WorkspaceMutationPolicy.Resolve(workspace.RootPath, destinationRelative, kind is not DocumentMutationKind.CreateFolder and not DocumentMutationKind.ImportAsset);
            ValidateAncestors(workspace.RootPath, source ?? destination!);
            if (destination is not null) ValidateAncestors(workspace.RootPath, destination);
            if (source is not null && !File.Exists(source)) throw new FileNotFoundException("The source document is missing.", source);
            if (kind != DocumentMutationKind.Enroll && destination is not null && (File.Exists(destination) || Directory.Exists(destination)))
                throw new IOException("The destination already exists; nothing was overwritten. Case-only rename is not supported.");
            if (kind == DocumentMutationKind.Rename && !string.Equals(Path.GetDirectoryName(source), Path.GetDirectoryName(destination), WorkspacePathBoundary.Comparison))
                throw new ArgumentException("Rename retains the original folder. Use Move to choose another folder.");
            if (kind == DocumentMutationKind.Enroll && source != destination) throw new ArgumentException("Enrollment must retain the source path.");
            if (kind == DocumentMutationKind.Recycle && new FileInfo(source!).Length > 20 * 1024 * 1024)
                throw new IOException("Protected recycling supports documents up to 20 MiB. The document was retained.");
            var original = source is null ? [] : await File.ReadAllBytesAsync(source, cancellationToken);
            if (reviewedHash is not null && Hash(original) != reviewedHash) throw new IOException("The document changed since review. Refresh before trying again.");
            Guid? identity = source is null ? null : DocumentIdCodec.Find(MarkdownIdentityBytes.Decode(original));
            if (creationText is not null && kind != DocumentMutationKind.Create)
                throw new ArgumentException("Initial text is only supported for document creation.");
            var output = creationText is null ? original : System.Text.Encoding.UTF8.GetBytes(creationText);
            if (kind is DocumentMutationKind.ImportAsset or DocumentMutationKind.ImportDocument or DocumentMutationKind.Restore)
            {
                if (sourceRelative is not null || importContent is null || destination is null)
                    throw new ArgumentException("Import requires a reviewed content snapshot and a new destination.");
                output = (byte[])importContent.Clone();
                if (kind == DocumentMutationKind.Restore) identity = DocumentIdCodec.Find(MarkdownIdentityBytes.Decode(output));
                if (kind == DocumentMutationKind.ImportDocument)
                {
                    identity = DocumentIdCodec.Find(MarkdownIdentityBytes.Decode(output));
                    if (identity is not null)
                    {
                        identity = Guid.NewGuid();
                        output = MarkdownIdentityBytes.Assign(output, identity.Value, true);
                    }
                }
            }
            else if (importContent is not null) throw new ArgumentException("Import content requires an import operation.");
            if (kind == DocumentMutationKind.Create && DocumentIdCodec.Find(creationText ?? "") is not null)
                throw new ArgumentException("New documents cannot inherit a template's identity. Enrollment is a separate action.");
            if (createdDocumentId is not null)
            {
                if (kind != DocumentMutationKind.Create || createdDocumentId == Guid.Empty)
                    throw new ArgumentException("A new identity is only valid for new document creation.");
                identity = createdDocumentId;
                var body = string.IsNullOrEmpty(creationText) ? Environment.NewLine : creationText;
                output = System.Text.Encoding.UTF8.GetBytes(DocumentIdCodec.Enroll(body, identity.Value));
            }
            if (kind == DocumentMutationKind.Duplicate && identity is not null)
            {
                var scan = await new BulkEnrollmentService(this).PreviewAsync(workspace, cancellationToken: cancellationToken);
                if (scan.Any(entry => entry.Identity == identity && entry.Status.Contains("Duplicate", StringComparison.Ordinal)))
                    throw new IOException("Resolve the source document's duplicate identity first.");
                identity = Guid.NewGuid();
                output = MarkdownIdentityBytes.Assign(original, identity.Value, true);
            }
            if (kind == DocumentMutationKind.Enroll)
            {
                if (identity is not null) return source;
                identity = Guid.NewGuid();
                output = MarkdownIdentityBytes.Assign(original, identity.Value, false);
            }
            if (kind == DocumentMutationKind.Duplicate && identity is null)
                MarkdownIdentityBytes.Assign(original, Guid.NewGuid(), false);
            var intent = new DocumentMutationIntent(Guid.NewGuid(), workspace.WorkspaceId, workspace.RootPath, kind, source, destination, Hash(output), identity)
            {
                RecoveryOperationId = recoveryOperationId
            };
            if (kind == DocumentMutationKind.Recycle)
            {
                if (state is not IDeletedDocumentStore deletedDocuments)
                    throw new IOException("Recovery capture is unavailable. The document was retained.");
                await deletedDocuments.PrepareDeletionAsync(intent, original, cancellationToken);
            }
            else if (kind == DocumentMutationKind.Restore)
            {
                if (state is not IDeletedDocumentStore deletedDocuments)
                    throw new IOException("The recovery store is unavailable.");
                await deletedDocuments.PrepareRestoreAsync(intent, cancellationToken);
            }
            else await state.PrepareMutationAsync(intent, cancellationToken);
            var mutated = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateAncestors(workspace.RootPath, source ?? destination!);
                if (destination is not null) ValidateAncestors(workspace.RootPath, destination);
                if (source is not null && Hash(await File.ReadAllBytesAsync(source, cancellationToken)) != Hash(original))
                    throw new IOException("The source changed before the operation. Refresh and try again.");
                switch (kind)
                {
                    case DocumentMutationKind.CreateFolder:
                        if (Directory.Exists(destination)) throw new IOException("The folder already exists.");
                        ExclusiveDirectoryCreator.Create(destination!);
                        break;
                    case DocumentMutationKind.Create:
                    case DocumentMutationKind.Duplicate:
                    case DocumentMutationKind.ImportAsset:
                    case DocumentMutationKind.ImportDocument:
                    case DocumentMutationKind.Restore:
                        await WriteNewAsync(destination!, output, cancellationToken);
                        break;
                    case DocumentMutationKind.Rename:
                    case DocumentMutationKind.Move:
                        File.Move(source!, destination!, false);
                        break;
                    case DocumentMutationKind.Recycle:
                        _recycle.Recycle(source!);
                        if (File.Exists(source)) throw new IOException("Windows retained the document; recycling was not completed.");
                        break;
                    case DocumentMutationKind.Enroll:
                        await ReplaceReviewedAsync(source!, original, output, cancellationToken);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(kind));
                }
                mutated = true;
                // Once the file transition completes, user cancellation cannot interrupt
                // reconciliation. A database failure leaves the durable intent for restart.
                await state.ReconcileMutationAsync(intent, true, CancellationToken.None);
                _log.Information("document.mutation." + kind, destination ?? source);
                return destination;
            }
            catch (Exception exception)
            {
                if (kind == DocumentMutationKind.Recycle && !File.Exists(source)) mutated = true;
                if (!mutated) await state.ReconcileMutationAsync(intent, false, CancellationToken.None);
                _log.Error("document.mutation.failed", exception, destination ?? source);
                if (mutated) throw new IOException("The file operation completed, but Axiom state needs reconciliation. Restart Axiom before another operation.", exception);
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<string>> ReconcilePendingAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<string>();
        foreach (var intent in await state.GetPendingMutationsAsync(cancellationToken))
        {
            if (intent.Kind == DocumentMutationKind.CopyFolder)
            {
                ValidateAncestors(intent.RootPath, intent.DestinationPath!, false);
                var copied = Directory.Exists(intent.DestinationPath) && await HashFolderAsync(intent.DestinationPath!, cancellationToken) == intent.ExpectedHash;
                if (Directory.Exists(intent.DestinationPath) && !copied || File.Exists(intent.DestinationPath))
                { results.Add("Needs inspection: copied folder changed. " + intent.DestinationPath); continue; }
                await state.ReconcileMutationAsync(intent, copied, cancellationToken);
                results.Add("Reconciled folder copy: " + (copied ? "verified copy present" : "no copy published"));
                continue;
            }
            if (intent.Kind == DocumentMutationKind.MoveFolder)
            {
                results.Add(await ReconcileFolderMoveAsync(intent, cancellationToken));
                continue;
            }
            if (intent.Kind == DocumentMutationKind.ExternalMove) ReviewedWorkspaceMove.ValidateSource(intent.SourcePath!);
            else ValidateAncestors(intent.RootPath, intent.SourcePath ?? intent.DestinationPath!, requireParent: false);
            if (intent.DestinationPath is not null) ValidateAncestors(intent.RootPath, intent.DestinationPath, requireParent: false);
            var applied = intent.Kind == DocumentMutationKind.Recycle
                ? !File.Exists(intent.SourcePath)
                : intent.Kind == DocumentMutationKind.CreateFolder
                    ? Directory.Exists(intent.DestinationPath)
                    : File.Exists(intent.DestinationPath) && Hash(await File.ReadAllBytesAsync(intent.DestinationPath!, cancellationToken)) == intent.ExpectedHash;
            if (intent.Kind is DocumentMutationKind.Move or DocumentMutationKind.Rename or DocumentMutationKind.ExternalMove && File.Exists(intent.SourcePath)) applied = false;
            if (!applied && intent.SourcePath is not null && !File.Exists(intent.SourcePath))
            {
                results.Add($"Needs inspection: {intent.Kind}, {intent.SourcePath}. No file operation was repeated.");
                continue;
            }
            await state.ReconcileMutationAsync(intent, applied, cancellationToken);
            results.Add($"Reconciled {intent.Kind}: {(applied ? "filesystem change present" : "no matching filesystem change")}: {intent.DestinationPath ?? intent.SourcePath}");
        }
        return results;
    }

    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    public async Task<string> RestoreAsync(WorkspaceRecord workspace, Guid recoveryOperationId, string? destinationRelative = null
                                        , bool newIdentity = false, CancellationToken cancellationToken = default)
    {
        if (state is not IDeletedDocumentStore deletedDocuments) throw new IOException("The recovery store is unavailable.");
        if ((await state.GetPendingMutationsAsync(cancellationToken)).Count > 0)
            throw new IOException("A previous operation needs reconciliation. Restart Axiom before restoring.");
        var item = (await deletedDocuments.GetDeletedDocumentsAsync(workspace.WorkspaceId, cancellationToken))
            .SingleOrDefault(candidate => candidate.Intent.OperationId == recoveryOperationId);
        if (item is null || item.Status != "Available") throw new IOException("This recovery item is unavailable or already restored.");
        if (!string.Equals(workspace.RootPath, item.Intent.RootPath, WorkspacePathBoundary.Comparison))
            throw new IOException("The recorded workspace root has changed. Recovery requires its original workspace.");
        if (Hash(item.Content) != item.Intent.ExpectedHash) throw new IOException("Recovery content failed its integrity check.");
        var relative = destinationRelative ?? Path.GetRelativePath(workspace.RootPath, item.Intent.SourcePath!);
        var destination = WorkspaceMutationPolicy.Resolve(workspace.RootPath, relative);
        if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("The restore destination exists. Choose another path; nothing was overwritten.");
        var content = item.Content;
        if (item.Intent.DocumentId is { } identity)
        {
            if (newIdentity) content = MarkdownIdentityBytes.Assign(content, Guid.NewGuid(), true);
            else
            {
                var discovery = await new FileDocumentStore().ListMarkdownFilesAsync(workspace.RootPath, cancellationToken);
                if (discovery.SkippedDirectories.Count > 0)
                    throw new IOException("The identity scan is incomplete. Resolve workspace access before preserving the identity.");
                var scan = await new BulkEnrollmentService(this).PreviewAsync(workspace, cancellationToken: cancellationToken);
                if (scan.Any(entry => entry.ReviewedHash is null || entry.Status.Contains("incomplete", StringComparison.OrdinalIgnoreCase)))
                    throw new IOException("The identity scan is incomplete. Resolve workspace access before preserving the identity.");
                if (scan.Any(entry => entry.Identity == identity))
                    throw new IOException("This document identity already exists. Restore with a new identity or resolve the conflict first.");
            }
        }
        var missing = new Stack<string>();
        for (var parent = Path.GetDirectoryName(destination); parent is not null && !Directory.Exists(parent); parent = Path.GetDirectoryName(parent))
            missing.Push(parent);
        var existing = missing.Count == 0 ? destination : missing.Peek();
        ValidateAncestors(workspace.RootPath, existing);
        while (missing.TryPop(out var folder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateAncestors(workspace.RootPath, folder);
            ExclusiveDirectoryCreator.Create(folder);
        }
        return (await ExecuteAsync(workspace, DocumentMutationKind.Restore, null, relative, cancellationToken: cancellationToken
            , importContent: content, recoveryOperationId: recoveryOperationId))!;
    }

    public static void ValidateAncestors(string root, string path, bool requireParent = true)
    {
        var normalizedRoot = WorkspaceService.NormalizeRootPath(root);
        if (!Directory.Exists(normalizedRoot)) throw new DirectoryNotFoundException("The workspace is unavailable.");
        if (!WorkspacePathBoundary.Contains(normalizedRoot, path, includeRoot: true))
            throw new ArgumentException("The target must remain inside its workspace.");
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("Linked files, folders, and ancestors cannot be changed through Axiom.");
        }
        var parent = Path.GetDirectoryName(path);
        if (requireParent && (parent is null || !Directory.Exists(parent))) throw new DirectoryNotFoundException("Choose an existing destination folder.");
    }

    private static async Task WriteNewAsync(string destination, byte[] content, CancellationToken cancellationToken)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".axiom-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await stream.WriteAsync(content, cancellationToken);
                stream.Flush(true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static async Task ReplaceReviewedAsync(string path, byte[] original, byte[] output, CancellationToken cancellationToken)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, $".axiom-{Guid.NewGuid():N}.tmp");
        try
        {
            await WriteNewAsync(temporary, output, cancellationToken);
            if (Hash(await File.ReadAllBytesAsync(path, cancellationToken)) != Hash(original)) throw new IOException("The document changed since preview; it was skipped.");
            cancellationToken.ThrowIfCancellationRequested();
            File.Replace(temporary, path, null);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

}
