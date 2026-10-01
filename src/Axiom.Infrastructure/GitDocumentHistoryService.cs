using System.Globalization;
using System.Text;
using Axiom.Core;

namespace Axiom.Infrastructure;

public sealed partial class GitDocumentHistoryService
{
    private readonly GitCommandRunner _git = new();

    public async Task<DocumentHistoryLocation?> LocateAsync(string documentPath, CancellationToken cancellationToken = default)
    {
        var path = Path.GetFullPath(documentPath);
        var directory = Path.GetDirectoryName(path)!;
        var result = await _git.RunAsync(directory, new[] { "rev-parse", "--show-toplevel" }, cancellationToken);
        if (result.ExitCode != 0)
        {
            if (result.Error.Contains("not a git repository", StringComparison.OrdinalIgnoreCase)) return null;
            throw new IOException(result.Error.Trim());
        }
        var root = Encoding.UTF8.GetString(result.Output).TrimEnd('\r', '\n');
        var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        ValidateRelativePath(relative);
        return new(root, relative);
    }

    public async Task<IReadOnlyList<DocumentRevision>> ListAsync(DocumentHistoryLocation location, CancellationToken cancellationToken = default)
    {
        ValidateRelativePath(location.RelativePath);
        var tips = new List<string>();
        foreach (var reference in new[] { "HEAD", HistoryReference(location) })
        {
            var resolved = await _git.RunAsync(location.RepositoryRoot, new[] { "rev-parse", "--verify", reference }, cancellationToken);
            if (resolved.ExitCode == 0) tips.Add(reference);
        }
        if (tips.Count == 0) return Array.Empty<DocumentRevision>();
        var arguments = new[]
        {
            "log", "-200", "--follow", "--no-ext-diff", "--no-textconv", "--format=%x1e%H%x00%cI%x00%s%x00", "--name-status", "-z"
        }.Concat(tips).Concat(new[] { "--", location.RelativePath }).ToArray();
        var result = await _git.RunAsync(location.RepositoryRoot, arguments, cancellationToken);
        if (result.ExitCode != 0)
        {
            if (result.Error.Contains("does not have any commits", StringComparison.OrdinalIgnoreCase)) return Array.Empty<DocumentRevision>();
            throw new IOException(result.Error.Trim());
        }
        var revisions = new List<DocumentRevision>();
        var currentPath = location.RelativePath;
        foreach (var record in Encoding.UTF8.GetString(result.Output).Split('\x1e', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = record.Split('\0');
            if (fields.Length < 4 || !IsCommitHash(fields[0])) throw new IOException("Git returned unsupported revision metadata.");
            var changedPath = currentPath;
            for (var index = 3; index < fields.Length - 1; index++)
            {
                var status = fields[index].TrimStart('\r', '\n');
                if (!status.StartsWith('R') || index + 2 >= fields.Length) continue;
                if (fields[index + 2] == currentPath)
                {
                    changedPath = fields[index + 1];
                    break;
                }
            }
            revisions.Add(new(fields[0], DateTimeOffset.Parse(fields[1], CultureInfo.InvariantCulture), fields[2], currentPath));
            ValidateRelativePath(changedPath);
            currentPath = changedPath;
        }
        return revisions;
    }

    public async Task<string> ReadAsync(DocumentHistoryLocation location, DocumentRevision revision, CancellationToken cancellationToken = default)
    {
        if (!IsCommitHash(revision.CommitHash)) throw new ArgumentException("A full Git commit hash is required.");
        ValidateRelativePath(revision.Path);
        var objectName = revision.CommitHash + ":" + revision.Path;
        var size = await RequireAsync(location.RepositoryRoot, new[] { "cat-file", "-s", objectName }, cancellationToken);
        if (!long.TryParse(Encoding.UTF8.GetString(size).Trim(), out var length) || length > 4 * 1024 * 1024)
            throw new IOException("Historical document exceeds the 4 MiB revision limit.");
        var bytes = await RequireAsync(location.RepositoryRoot, new[] { "cat-file", "blob", objectName }, cancellationToken);
        using var stream = new MemoryStream(bytes);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private async Task<byte[]> RequireAsync(string directory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var result = await _git.RunAsync(directory, arguments, cancellationToken);
        if (result.ExitCode != 0) throw new IOException(result.Error.Trim());
        return result.Output;
    }

    private static bool IsCommitHash(string value) => value.Length is 40 or 64 && value.All(char.IsAsciiHexDigit);

    private static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains('\\') || path.Any(char.IsControl) ||
            path.Split('/').Any(part => part is ".." or "." || part.Equals(".git", StringComparison.OrdinalIgnoreCase) || part.Length == 0))
            throw new IOException("Unsupported document path for Git history.");
    }
}
