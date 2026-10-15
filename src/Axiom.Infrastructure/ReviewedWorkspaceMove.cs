using System.Text;
using System.Text.Json;
using Axiom.Core;

namespace Axiom.Infrastructure;

public sealed record MoveReferenceFile(string Source, string Destination, byte[] Before, byte[] After, IReadOnlyList<ReferenceEdit> Edits);
public sealed record ReviewedMove(WorkspaceRecord Workspace, string Source, string Destination, bool Folder,
    string ReviewedHash, IReadOnlyList<MoveReferenceFile> Files, IReadOnlyList<string> Warnings);

/// <summary>Approved repair content is receipted before moving; restart finishes only matching reviewed bytes.</summary>
public sealed class ReviewedWorkspaceMove(SqliteStateStore state)
{
    private string Receipt => Path.Combine(Path.GetDirectoryName(state.Paths.DatabasePath)!, "reviewed-move.json");
    public bool Pending => File.Exists(Receipt);

    public async Task<ReviewedMove> ReviewAsync(WorkspaceRecord workspace, string source, string destination, CancellationToken token = default)
    {
        if (Pending || (await state.GetPendingMutationsAsync(token)).Count > 0) throw new IOException("Restart to reconcile the previous move before continuing.");
        source = Path.GetFullPath(source);
        destination = WorkspaceMutationPolicy.Resolve(workspace.RootPath, Path.GetRelativePath(workspace.RootPath, destination), false);
        ValidateSource(source);
        WorkspaceFileOperations.ValidateAncestors(workspace.RootPath, destination);
        var folder = Directory.Exists(source);
        if (WorkspacePathBoundary.Contains(state.Paths.Root, source, true) || WorkspacePathBoundary.Contains(state.Paths.Root, destination, true) || folder && WorkspacePathBoundary.Contains(source, state.Paths.Root, true))
            throw new IOException("App state cannot be moved or used as a workspace move destination.");
        if (!folder && !File.Exists(source)) throw new IOException("The source is missing.");
        if (folder && !WorkspacePathBoundary.Contains(workspace.RootPath, source)) throw new IOException("Only workspace folders can be moved.");
        if (WorkspacePathBoundary.Contains(source, destination, true) || File.Exists(destination) || Directory.Exists(destination)) throw new IOException("Destination exists or is inside the source. Nothing was changed.");
        string Map(string path) => path.Equals(source, WorkspacePathBoundary.Comparison) ? destination
            : folder && WorkspacePathBoundary.Contains(source, path) ? Path.Combine(destination, Path.GetRelativePath(source, path)) : path;
        var discovery = await new FileDocumentStore().ListMarkdownFilesAsync(workspace.RootPath, token);
        if (discovery.SkippedDirectories.Count > 0) throw new IOException("The workspace scan is incomplete. Resolve inaccessible or linked folders first.");
        var paths = discovery.Files.Concat(folder ? [] : new[] { source }).Distinct(WorkspacePathBoundary.Comparer).Order(WorkspacePathBoundary.Comparer).ToArray();
        if (paths.Length > 10000) throw new IOException("Review supports up to 10,000 Markdown documents.");
        var files = new List<MoveReferenceFile>();
        long totalBytes = 0;
        var warnings = new List<string>();
        foreach (var path in paths)
        {
            token.ThrowIfCancellationRequested();
            ValidateSource(path);
            if (new FileInfo(path).Length > 20 * 1024 * 1024) throw new IOException("A document exceeds the 20 MiB repair review limit.");
            var bytes = await File.ReadAllBytesAsync(path, token);
            totalBytes += bytes.Length;
            if (totalBytes > 64 * 1024 * 1024) throw new IOException("Reference review supports up to 64 MiB of Markdown per workspace. No files were changed.");
            string text;
            try { text = MarkdownIdentityBytes.Decode(bytes); }
            catch (Exception error) when (error is DecoderFallbackException or IOException)
            { throw new IOException($"Repair requires UTF-8 Markdown: {path}. No files were changed.", error); }
            var repair = RelativeReferenceRepair.Plan(text, path, Map(path), Map, target => File.Exists(target) || Directory.Exists(target));
            warnings.AddRange(repair.Warnings.Select(warning => Path.GetRelativePath(workspace.RootPath, path) + ": " + warning));
            var after = Encoding.UTF8.GetBytes(repair.Text);
            if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) after = new byte[] { 0xef, 0xbb, 0xbf }.Concat(after).ToArray();
            // Include every reviewed document so changes/new links cannot slip in between review and apply.
            files.Add(new(path, Map(path), bytes, after, repair.Edits));
        }
        var operations = new WorkspaceFileOperations(state);
        var hash = folder ? await operations.ReviewFolderAsync(workspace, Path.GetRelativePath(workspace.RootPath, source), token)
            : WorkspaceFileOperations.Hash(await File.ReadAllBytesAsync(source, token));
        warnings.Add("Only this workspace and the explicitly moved document are scanned. External incoming links, raw HTML and wiki links are not rewritten.");
        return new(workspace, source, destination, folder, hash, files, warnings);
    }

    public async Task ApplyAsync(ReviewedMove plan, bool repair, CancellationToken token = default)
    {
        if (Pending) throw new IOException("A move receipt is already pending.");
        // Rebuild to validate the full source tree, destination and newly added inbound links.
        var fresh = await ReviewAsync(plan.Workspace, plan.Source, plan.Destination, token);
        if (fresh.ReviewedHash != plan.ReviewedHash || fresh.Files.Count != plan.Files.Count || fresh.Files.Zip(plan.Files).Any(pair => pair.First.Source != pair.Second.Source || !pair.First.Before.SequenceEqual(pair.Second.Before)))
            throw new IOException("Files changed since review. Review the move again; nothing was changed.");
        var approved = repair ? plan : plan with { Files = plan.Files.Select(file => file with { After = file.Before, Edits = [] }).ToArray() };
        var temp = Receipt + ".tmp";
        await using (var receipt = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(receipt, approved, cancellationToken: token);
            receipt.Flush(flushToDisk: true);
        }
        File.Move(temp, Receipt, false);
        var operations = new WorkspaceFileOperations(state);
        try
        {
            if (plan.Folder) await operations.MoveFolderAsync(plan.Workspace, Path.GetRelativePath(plan.Workspace.RootPath, plan.Source), Path.GetRelativePath(plan.Workspace.RootPath, plan.Destination), plan.ReviewedHash, token);
            else if (WorkspacePathBoundary.Contains(plan.Workspace.RootPath, plan.Source))
                await operations.ExecuteAsync(plan.Workspace, DocumentMutationKind.Move, Path.GetRelativePath(plan.Workspace.RootPath, plan.Source), Path.GetRelativePath(plan.Workspace.RootPath, plan.Destination), plan.ReviewedHash, token);
            else await MoveExternalAsync(plan, token);
            await FinishRepairsAsync(approved);
            File.Delete(Receipt);
        }
        catch
        {
            if ((File.Exists(plan.Source) || Directory.Exists(plan.Source)) && !File.Exists(plan.Destination) && !Directory.Exists(plan.Destination)) File.Delete(Receipt);
            throw;
        }
    }

    private async Task MoveExternalAsync(ReviewedMove plan, CancellationToken token)
    {
        ValidateSource(plan.Source);
        WorkspaceFileOperations.ValidateAncestors(plan.Workspace.RootPath, plan.Destination);
        var intent = new DocumentMutationIntent(Guid.NewGuid(), plan.Workspace.WorkspaceId, plan.Workspace.RootPath,
            DocumentMutationKind.ExternalMove, plan.Source, plan.Destination, plan.ReviewedHash,
            DocumentIdCodec.Find(MarkdownIdentityBytes.Decode(await File.ReadAllBytesAsync(plan.Source, token))));
        await state.PrepareMutationAsync(intent, token);
        var moved = false;
        try
        {
            if (WorkspaceFileOperations.Hash(await File.ReadAllBytesAsync(plan.Source, token)) != plan.ReviewedHash) throw new IOException("Source changed since review.");
            File.Move(plan.Source, plan.Destination, false);
            moved = true;
            await state.ReconcileMutationAsync(intent, true, CancellationToken.None);
        }
        catch { if (!moved) await state.ReconcileMutationAsync(intent, false, CancellationToken.None); throw; }
    }

    public async Task RecoverAsync()
    {
        if (!Pending) return;
        if ((await state.GetPendingMutationsAsync()).Count > 0) throw new IOException("Resolve the pending move receipt before reference repairs.");
        var plan = JsonSerializer.Deserialize<ReviewedMove>(await File.ReadAllTextAsync(Receipt)) ?? throw new IOException("Move receipt is unreadable.");
        if ((File.Exists(plan.Source) || Directory.Exists(plan.Source)) && !File.Exists(plan.Destination) && !Directory.Exists(plan.Destination)) { File.Delete(Receipt); return; }
        if (File.Exists(plan.Source) || Directory.Exists(plan.Source) || !File.Exists(plan.Destination) && !Directory.Exists(plan.Destination)) throw new IOException("Inspect the retained move receipt; source/destination are ambiguous.");
        await FinishRepairsAsync(plan);
        File.Delete(Receipt);
    }

    private static async Task FinishRepairsAsync(ReviewedMove plan)
    {
        foreach (var file in plan.Files.Where(file => !file.Before.SequenceEqual(file.After)))
        {
            WorkspaceFileOperations.ValidateAncestors(plan.Workspace.RootPath, file.Destination);
            var current = await File.ReadAllBytesAsync(file.Destination);
            if (current.SequenceEqual(file.After)) continue;
            if (!current.SequenceEqual(file.Before)) throw new IOException($"Repair paused: {file.Destination} changed. Original and approved bytes are retained in the move receipt.");
            await WorkspaceFileOperations.ReplaceReviewedAsync(file.Destination, file.Before, file.After, CancellationToken.None);
        }
    }

    public static void ValidateSource(string source)
    {
        var root = Path.GetPathRoot(source)!;
        WorkspaceFileOperations.ValidateAncestors(root, source);
        if ((File.Exists(source) || Directory.Exists(source)) && File.GetAttributes(source).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Linked sources cannot be moved or rewritten.");
    }
}
