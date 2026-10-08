namespace Axiom.Infrastructure;

public static class WorkspaceFolderDiscovery
{
    public static IReadOnlyList<string> List(string root, CancellationToken cancellationToken = default)
    {
        var result = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                foreach (var child in Directory.EnumerateDirectories(current))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (Path.GetFileName(child).Equals(".git", StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        if (File.GetAttributes(child).HasFlag(FileAttributes.ReparsePoint)) continue;
                        // Explicit folder nodes represent genuinely empty directories; Markdown
                        // discovery supplies populated branches separately. Ancestors are built by the tree.
                        if (!Directory.EnumerateFileSystemEntries(child).Any()) result.Add(child);
                        pending.Push(child);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
        return result;
    }
}
