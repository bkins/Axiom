using System.Security.Cryptography;
using System.Text;
using Axiom.Core;

namespace Axiom.Infrastructure;

public sealed partial class GitDocumentHistoryService
{
    public async Task InitializeAsync(string workspaceRoot, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(workspaceRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new IOException("Git setup requires a direct workspace directory.");
        if (await LocateAsync(Path.Combine(root, "axiom-history-check.md"), cancellationToken) is not null)
            throw new IOException("A Git repository already covers this workspace; it was not reinitialized.");
        await RequireAsync(root, new[] { "-c", "init.templateDir=", "init", "--quiet", "--", root }, cancellationToken);
    }

    public async Task<byte[]> ReviewCheckpointAsync(DocumentHistoryLocation location, CancellationToken cancellationToken = default)
    {
        ValidateRelativePath(location.RelativePath);
        var path = Path.Combine(location.RepositoryRoot, location.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        var current = location.RepositoryRoot;
        foreach (var part in location.RelativePath.Split('/'))
        {
            current = Path.Combine(current, part);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Checkpointing linked files or directories is not supported.");
        }
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous);
        if (stream.Length > 4 * 1024 * 1024) throw new IOException("Checkpoint exceeds the 4 MiB revision limit.");
        using var output = new MemoryStream();
        await stream.CopyToAsync(output, cancellationToken);
        if (output.Length > 4 * 1024 * 1024) throw new IOException("Checkpoint exceeds the 4 MiB revision limit.");
        return output.ToArray();
    }

    public async Task<string> CheckpointAsync(DocumentHistoryLocation location, byte[] reviewedBytes, CancellationToken cancellationToken = default)
    {
        var snapshot = reviewedBytes.ToArray();
        if (!(await ReviewCheckpointAsync(location, cancellationToken)).AsSpan().SequenceEqual(snapshot))
            throw new IOException("The saved document changed after checkpoint review. Review it again before creating a checkpoint.");
        var reference = HistoryReference(location);
        var previous = await _git.RunAsync(location.RepositoryRoot, new[] { "rev-parse", "--verify", reference }, cancellationToken);
        var oldReference = previous.ExitCode == 0 ? Encoding.UTF8.GetString(previous.Output).Trim() : null;
        var parent = oldReference;
        if (parent is null)
        {
            var head = await _git.RunAsync(location.RepositoryRoot, new[] { "rev-parse", "--verify", "HEAD" }, cancellationToken);
            if (head.ExitCode == 0) parent = Encoding.UTF8.GetString(head.Output).Trim();
        }
        var index = Path.Combine(Path.GetTempPath(), "AxiomGitIndex-" + Guid.NewGuid().ToString("N"));
        async Task<string> Run(IReadOnlyList<string> arguments, byte[]? input = null)
        {
            var result = await _git.RunAsync(location.RepositoryRoot, arguments, cancellationToken, input, index);
            if (result.ExitCode != 0) throw new IOException(result.Error.Trim());
            return Encoding.UTF8.GetString(result.Output).Trim();
        }
        try
        {
            await Run(parent is null ? new[] { "read-tree", "--empty" } : new[] { "read-tree", parent });
            var mode = "100644";
            if (parent is not null)
            {
                var entry = await Run(new[] { "ls-tree", "-z", parent, "--", location.RelativePath });
                if (entry.StartsWith("100755 ", StringComparison.Ordinal)) mode = "100755";
                else if (entry.Length > 0 && !entry.StartsWith("100644 ", StringComparison.Ordinal))
                    throw new IOException("Only regular Markdown files can be checkpointed.");
            }
            var blob = await Run(new[] { "hash-object", "-w", "--stdin" }, snapshot);
            var entryPath = Encoding.UTF8.GetBytes($"{mode} {blob}\t{location.RelativePath}\0");
            await Run(new[] { "update-index", "-z", "--index-info" }, entryPath);
            var tree = await Run(new[] { "write-tree" });
            var commitArguments = new List<string>
            {
                "-c", "commit.gpgsign=false", "-c", "user.name=Axiom checkpoint", "-c", "user.email=checkpoint@axiom.local", "commit-tree", tree
            };
            if (parent is not null) { commitArguments.Add("-p"); commitArguments.Add(parent); }
            commitArguments.Add("-m");
            commitArguments.Add("Axiom selected-document checkpoint: " + location.RelativePath);
            var commit = await Run(commitArguments);
            cancellationToken.ThrowIfCancellationRequested();
            // Publication is one compare-and-swap ref update. Once started, finish it without cancellation ambiguity.
            await RequireAsync(location.RepositoryRoot, new[]
            {
                "update-ref", "--create-reflog", "-m", "Axiom selected-document checkpoint", reference, commit, oldReference ?? new string('0', commit.Length)
            }, CancellationToken.None);
            return commit;
        }
        finally
        {
            File.Delete(index);
            File.Delete(index + ".lock");
        }
    }

    private static string HistoryReference(DocumentHistoryLocation location) =>
        "refs/axiom/document-history/" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(location.RelativePath))).ToLowerInvariant();
}
