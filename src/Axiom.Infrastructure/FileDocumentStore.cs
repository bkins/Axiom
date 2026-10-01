using System.Text;
using Axiom.Core;

namespace Axiom.Infrastructure;

public sealed class FileDocumentStore(IAxiomLog? log = null) : IDocumentFileStore
{
    private readonly IAxiomLog _log = log ?? new NullAxiomLog();

    public Task<MarkdownDiscoveryResult> ListMarkdownFilesAsync(string folder, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(folder);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"The workspace folder does not exist: {root}");
        var paths = new List<string>();
        var skipped = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                paths.AddRange(Directory.EnumerateFiles(current)
                    .Where(path => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                                   path.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase)));
                foreach (var child in Directory.EnumerateDirectories(current))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        if (File.GetAttributes(child).HasFlag(FileAttributes.ReparsePoint))
                            skipped.Add(child);
                        else
                            pending.Push(child);
                    }
                    catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
                    {
                        skipped.Add(child);
                    }
                }
            }
            catch (Exception exception) when (current != root && exception is UnauthorizedAccessException or IOException)
            {
                skipped.Add(current);
            }
        }
        return Task.FromResult(new MarkdownDiscoveryResult(
            paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
            skipped.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray()));
    }

    public async Task<DocumentFile> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        var prefix = new byte[4];
        var prefixLength = await stream.ReadAtLeastAsync(prefix, 4, throwOnEndOfStream: false, cancellationToken);
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = await reader.ReadToEndAsync(cancellationToken);
        var hasBom = prefixLength >= 3 && prefix[0] == 0xef && prefix[1] == 0xbb && prefix[2] == 0xbf ||
            prefixLength >= 2 && (prefix[0] == 0xff && prefix[1] == 0xfe || prefix[0] == 0xfe && prefix[1] == 0xff) ||
            prefixLength >= 4 && prefix[0] == 0 && prefix[1] == 0 && prefix[2] == 0xfe && prefix[3] == 0xff;
        var info = new FileInfo(fullPath);
        _log.Information("file.read", fullPath);
        return new DocumentFile(fullPath, text, info.LastWriteTimeUtc, info.Length)
        {
            ObservedEncoding = reader.CurrentEncoding.WebName.ToUpperInvariant() + (hasBom ? " (BOM)" : " (assumed; no BOM)")
        };
    }

    public async Task<DocumentFile> WriteAsync(string path, string text, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("The selected path has no parent directory.");
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(tempPath, text, new UTF8Encoding(false), cancellationToken);
            if (File.Exists(fullPath))
            {
                var backupPath = tempPath + ".bak";
                File.Replace(tempPath, fullPath, backupPath, true);
                File.Delete(backupPath);
            }
            else
            {
                File.Move(tempPath, fullPath);
            }
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }

        _log.Information("file.written", fullPath);
        return await ReadAsync(fullPath, cancellationToken);
    }

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(path));
    }
}
