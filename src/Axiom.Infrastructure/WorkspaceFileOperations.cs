using System.Security.Cryptography;
using Axiom.Core;

namespace Axiom.Infrastructure;

public sealed class WorkspaceFileOperations(IDocumentMutationStore state
                                         , IRecycleBin? recycle = null
                                         , IAxiomLog? log = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IRecycleBin _recycle = recycle ?? new WindowsRecycleBin();
    private readonly IAxiomLog _log = log ?? new NullAxiomLog();

    public async Task<string?> ExecuteAsync(WorkspaceRecord workspace
                                         , DocumentMutationKind kind
                                         , string? sourceRelative
                                         , string? destinationRelative
                                         , string? reviewedHash = null
                                         , CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if ((await state.GetPendingMutationsAsync(cancellationToken)).Count > 0)
                throw new IOException("A previous file operation needs reconciliation. Restart Axiom before making another change.");
            var source = sourceRelative is null ? null : WorkspaceMutationPolicy.Resolve(workspace.RootPath, sourceRelative);
            var destination = destinationRelative is null ? null : WorkspaceMutationPolicy.Resolve(workspace.RootPath, destinationRelative, kind != DocumentMutationKind.CreateFolder);
            ValidateAncestors(workspace.RootPath, source ?? destination!);
            if (destination is not null) ValidateAncestors(workspace.RootPath, destination);
            if (source is not null && !File.Exists(source)) throw new FileNotFoundException("The source document is missing.", source);
            if (kind != DocumentMutationKind.Enroll && destination is not null && (File.Exists(destination) || Directory.Exists(destination)))
                throw new IOException("The destination already exists; nothing was overwritten. Case-only rename is not supported.");
            if (kind == DocumentMutationKind.Rename && !string.Equals(Path.GetDirectoryName(source), Path.GetDirectoryName(destination), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Rename retains the original folder. Use Move to choose another folder.");
            if (kind == DocumentMutationKind.Enroll && source != destination) throw new ArgumentException("Enrollment must retain the source path.");
            var original = source is null ? [] : await File.ReadAllBytesAsync(source, cancellationToken);
            if (reviewedHash is not null && Hash(original) != reviewedHash) throw new IOException("The document changed since review. Refresh before trying again.");
            Guid? identity = source is null ? null : DocumentIdCodec.Find(MarkdownIdentityBytes.Decode(original));
            var output = original;
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
            var intent = new DocumentMutationIntent(Guid.NewGuid(), workspace.WorkspaceId, workspace.RootPath, kind, source, destination, Hash(output), identity);
            await state.PrepareMutationAsync(intent, cancellationToken);
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
                        CreateDirectoryExclusive(destination!);
                        break;
                    case DocumentMutationKind.Create:
                    case DocumentMutationKind.Duplicate:
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
            ValidateAncestors(intent.RootPath, intent.SourcePath ?? intent.DestinationPath!);
            if (intent.DestinationPath is not null) ValidateAncestors(intent.RootPath, intent.DestinationPath);
            var applied = intent.Kind == DocumentMutationKind.Recycle
                ? !File.Exists(intent.SourcePath)
                : intent.Kind == DocumentMutationKind.CreateFolder
                    ? Directory.Exists(intent.DestinationPath)
                    : File.Exists(intent.DestinationPath) && Hash(await File.ReadAllBytesAsync(intent.DestinationPath!, cancellationToken)) == intent.ExpectedHash;
            if (intent.Kind is DocumentMutationKind.Move or DocumentMutationKind.Rename && File.Exists(intent.SourcePath)) applied = false;
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

    public static void ValidateAncestors(string root, string path)
    {
        var normalizedRoot = WorkspaceService.NormalizeRootPath(root);
        var rootPrefix = Path.EndsInDirectorySeparator(normalizedRoot) ? normalizedRoot : normalizedRoot + Path.DirectorySeparatorChar;
        if (!Directory.Exists(normalizedRoot)) throw new DirectoryNotFoundException("The workspace is unavailable.");
        if (!string.Equals(path, normalizedRoot, StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The target must remain inside its workspace.");
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("Linked files, folders, and ancestors cannot be changed through Axiom.");
        }
        var parent = Path.GetDirectoryName(path);
        if (parent is null || !Directory.Exists(parent)) throw new DirectoryNotFoundException("Choose an existing destination folder.");
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

    private static async Task ReplaceReviewedAsync(string path, byte[] original, byte[] output, CancellationToken cancellationToken)
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

    private static void CreateDirectoryExclusive(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Exclusive folder creation requires Windows.");
        if (!CreateDirectory(path, nint.Zero)) throw new IOException("Folder creation failed; the target may already exist.", new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error()));
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool CreateDirectory(string path, nint securityAttributes);
}
