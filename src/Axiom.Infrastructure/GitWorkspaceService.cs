using System.Text;
using Axiom.Core;

namespace Axiom.Infrastructure;

public sealed partial class GitWorkspaceService
{
    private readonly GitCommandRunner _git = new();

    public async Task<GitRepositorySnapshot?> InspectAsync(string workspaceRoot, CancellationToken cancellationToken = default)
    {
        var workspace = WorkspaceService.NormalizeRootPath(workspaceRoot);
        var location = await new GitDocumentHistoryService().LocateAsync(Path.Combine(workspace, ".axiom-git-inspection.md"), cancellationToken);
        if (location is null) return null;
        var root = location.RepositoryRoot;
        var branchResult = await _git.RunAsync(root, new[] { "symbolic-ref", "--quiet", "HEAD" }, cancellationToken);
        var branch = branchResult.ExitCode == 0 ? Decode(branchResult.Output).Trim() : "Detached HEAD";
        var headResult = await _git.RunAsync(root, new[] { "rev-parse", "--verify", "HEAD" }, cancellationToken);
        var head = headResult.ExitCode == 0 ? Decode(headResult.Output).Trim() : null;
        var administrationNames = new[] { "index", "MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "rebase-merge", "rebase-apply" };
        var pathArguments = new[] { "rev-parse", "--path-format=absolute" }
            .Concat(administrationNames.SelectMany(name => new[] { "--git-path", name })).ToArray();
        var administrationPaths = (await RequireAsync(root, pathArguments, cancellationToken)).TrimEnd('\r', '\n').Split('\n')
            .Select(path => Path.GetFullPath(path.TrimEnd('\r'))).ToArray();
        if (administrationPaths.Length != administrationNames.Length) throw new IOException("Git returned incomplete administration paths.");
        var indexPath = administrationPaths[0];
        var reasons = new List<string>();
        if (!branch.StartsWith("refs/heads/", StringComparison.Ordinal)) reasons.Add("Detached HEAD: use external Git to select a branch before staging/committing.");
        if ((await RequireAsync(root, new[] { "ls-files", "--unmerged", "-z" }, cancellationToken)).Length > 0) reasons.Add("Resolve repository conflicts with external Git first.");
        if ((await RequireAsync(root, new[] { "rev-parse", "--show-superproject-working-tree" }, cancellationToken)).Trim().Length > 0) reasons.Add("Submodule repositories are read-only in Axiom.");
        var sparse = await _git.RunAsync(root, new[] { "config", "--bool", "core.sparseCheckout" }, cancellationToken);
        if (sparse.ExitCode == 0 && Decode(sparse.Output).Trim() == "true") reasons.Add("Sparse checkout mutation requires external Git.");
        foreach (var path in administrationPaths.Skip(1))
        {
            if (File.Exists(path) || Directory.Exists(path)) reasons.Add("Finish the pending Git operation before Axiom mutations.");
        }
        var fields = (await RequireAsync(root, new[] { "status", "--porcelain=v1", "-z", "--untracked-files=all" }, cancellationToken)).Split('\0');
        var changed = new List<GitChangedFile>();
        for (var index = 0; index < fields.Length; index++)
        {
            var field = fields[index];
            if (field.Length == 0) continue;
            if (field.Length < 4 || field[2] != ' ') throw new IOException("Git returned unsupported status data.");
            var relative = field[3..];
            string? original = null;
            if (field[0] is 'R' or 'C' || field[1] is 'R' or 'C')
            {
                if (++index >= fields.Length) throw new IOException("Git returned incomplete rename data.");
                original = fields[index];
            }
            var absolute = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            var workspaceRelative = Path.GetRelativePath(workspace, absolute);
            string? reason = null;
            try
            {
                var managed = WorkspaceMutationPolicy.Resolve(workspace, workspaceRelative);
                WorkspaceFileOperations.ValidateAncestors(workspace, managed, requireParent: false);
                if (File.Exists(managed) && new FileInfo(managed).Length > 4 * 1024 * 1024)
                    reason = "Documents larger than 4 MiB require external Git.";
            }
            catch (Exception exception) when (exception is ArgumentException or IOException)
            { workspaceRelative = null; reason = "Only regular Markdown documents inside the active workspace can be managed. " + exception.Message; }
            if (original is not null) reason = "Inspect renames/copies here; manage their coordinated paths through external Git.";
            if (field[0] == 'U' || field[1] == 'U' || field[..2] is "AA" or "DD") reason = "Resolve this conflict with external Git.";
            changed.Add(new(relative, workspaceRelative, field[..1], field.Substring(1, 1), original, reason is null, reason));
        }
        var candidates = changed.Where(file => file.CanManage).ToArray();
        if (candidates.Length > 0)
        {
            var input = Encoding.UTF8.GetBytes(string.Join('\0', candidates.Select(file => file.RelativePath)) + "\0");
            var attributes = (await RequireAsync(root, new[] { "check-attr", "-z", "--stdin", "filter" }, cancellationToken, input)).Split('\0');
            for (var index = 0; index + 2 < attributes.Length; index += 3)
            {
                if (attributes[index + 2] is "unspecified" or "unset") continue;
                var position = changed.FindIndex(file => file.RelativePath == attributes[index]);
                if (position >= 0) changed[position] = changed[position] with { CanManage = false, Reason = "Git clean/LFS filters require external Git." };
            }
            var entries = (await RequireAsync(root, new[] { "ls-files", "--stage", "-z" }, cancellationToken)).Split('\0', StringSplitOptions.RemoveEmptyEntries);
            foreach (var entry in entries)
            {
                var separator = entry.IndexOf('\t');
                if (separator < 0) throw new IOException("Git returned incomplete index entries.");
                var position = changed.FindIndex(file => file.RelativePath == entry[(separator + 1)..]);
                if (position < 0 || !changed[position].CanManage) continue;
                try { ParseEntry(entry); }
                catch (IOException exception) { changed[position] = changed[position] with { CanManage = false, Reason = exception.Message }; }
            }
        }
        return new(workspace, root, branch, head, indexPath, await IndexHashAsync(indexPath, cancellationToken), changed, reasons.Count == 0, reasons.Count == 0 ? null : string.Join(" ", reasons.Distinct()));
    }

    public async Task<GitDocumentDiff> ReadDiffAsync(GitRepositorySnapshot snapshot, string relativePath, CancellationToken cancellationToken = default)
    {
        var path = ManagedPath(snapshot, relativePath);
        var current = File.Exists(path) ? DecodeDocument(await ReadWorkingAsync(path, cancellationToken)) : "";
        var entry = await IndexEntryAsync(snapshot.RepositoryRoot, relativePath, cancellationToken);
        var indexed = entry is null ? "" : await ReadBlobAsync(snapshot.RepositoryRoot, ParseEntry(entry).Hash, cancellationToken);
        var tree = snapshot.Head is null ? "" : await RequireAsync(snapshot.RepositoryRoot, new[] { "ls-tree", "-z", snapshot.Head, "--", relativePath }, cancellationToken);
        var baseline = tree.Length == 0 ? "" : await ReadBlobAsync(snapshot.RepositoryRoot, tree.Split('\t')[0].Split(' ')[2], cancellationToken);
        return new(baseline, indexed, current);
    }

    public async Task<string?> ReviewWorkingHashAsync(GitRepositorySnapshot snapshot, string relativePath, CancellationToken cancellationToken = default)
    {
        var path = ManagedPath(snapshot, relativePath);
        return File.Exists(path) ? WorkspaceFileOperations.Hash(await ReadWorkingAsync(path, cancellationToken)) : null;
    }

    private static string ManagedPath(GitRepositorySnapshot snapshot, string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(snapshot.RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(snapshot.WorkspaceRoot, path);
        var managed = WorkspaceMutationPolicy.Resolve(snapshot.WorkspaceRoot, relative);
        WorkspaceFileOperations.ValidateAncestors(snapshot.WorkspaceRoot, managed, requireParent: false);
        return managed;
    }

    private async Task<string?> IndexEntryAsync(string root, string path, CancellationToken cancellationToken)
    {
        var entries = (await RequireAsync(root, new[] { "ls-files", "--stage", "-z", "--", path }, cancellationToken)).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        if (entries.Length == 0) return null;
        if (entries.Length != 1) throw new IOException("Resolve the selected document's Git conflict first.");
        ParseEntry(entries[0]);
        return entries[0];
    }

    private static (string Mode, string Hash) ParseEntry(string entry)
    {
        var fields = entry.Split('\t')[0].Split(' ');
        if (fields.Length != 3 || fields[0] is not "100644" and not "100755" || fields[2] != "0" || fields[1].Length is not 40 and not 64 || !fields[1].All(char.IsAsciiHexDigit))
            throw new IOException("Only unconflicted regular Git document entries are supported.");
        return (fields[0], fields[1]);
    }

    private async Task<string> ReadBlobAsync(string root, string hash, CancellationToken cancellationToken)
    {
        var size = await RequireAsync(root, new[] { "cat-file", "-s", hash }, cancellationToken);
        if (!long.TryParse(size.Trim(), out var length) || length > 4 * 1024 * 1024) throw new IOException("Git document diff exceeds the 4 MiB limit.");
        var result = await _git.RunAsync(root, new[] { "cat-file", "blob", hash }, cancellationToken);
        if (result.ExitCode != 0) throw new IOException(result.Error.Trim());
        return DecodeDocument(result.Output);
    }

    private async Task<string> GitPathAsync(string root, string name, CancellationToken cancellationToken)
    {
        var path = (await RequireAsync(root, new[] { "rev-parse", "--git-path", name }, cancellationToken)).TrimEnd('\r', '\n');
        return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
    }

    private async Task<string> RequireAsync(string root, IReadOnlyList<string> arguments, CancellationToken cancellationToken, byte[]? input = null, string? indexPath = null)
    {
        var hooks = Path.Combine(Path.GetTempPath(), "AxiomGitEmptyHooks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(hooks);
        try
        {
            var safeArguments = new[] { "-c", "core.hooksPath=" + hooks }.Concat(arguments).ToArray();
            var result = await _git.RunAsync(root, safeArguments, cancellationToken, input, indexPath);
            if (result.ExitCode != 0) throw new IOException(result.Error.Trim());
            return Decode(result.Output);
        }
        finally { Directory.Delete(hooks); }
    }

    private static string Decode(byte[] content) => Encoding.UTF8.GetString(content);
    private static string DecodeDocument(byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static async Task<byte[]> ReadWorkingAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous);
        if (stream.Length > 4 * 1024 * 1024) throw new IOException("Git document exceeds the 4 MiB limit.");
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > 4 * 1024 * 1024) throw new IOException("Git document exceeds the 4 MiB limit.");
        return buffer.ToArray();
    }

    private static async Task<string> IndexHashAsync(string indexPath, CancellationToken cancellationToken) =>
        File.Exists(indexPath) ? WorkspaceFileOperations.Hash(await File.ReadAllBytesAsync(indexPath, cancellationToken)) : "Missing";
}
