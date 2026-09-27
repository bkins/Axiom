namespace Axiom.Core;

public sealed record WorkspaceRecord(
    Guid WorkspaceId,
    string RootPath,
    string DisplayName,
    DateTimeOffset CreatedUtc,
    DateTimeOffset LastOpenedUtc);

public sealed class WorkspaceTreeNode(
    string name,
    string relativePath,
    bool isDocument,
    IReadOnlyList<WorkspaceTreeNode> children)
{
    public string Name { get; } = name;
    public string RelativePath { get; } = relativePath;
    public bool IsDocument { get; } = isDocument;
    public IReadOnlyList<WorkspaceTreeNode> Children { get; } = children;
    public bool IsExpanded { get; set; }
}

public static class WorkspaceTreeBuilder
{
    public static IReadOnlyList<WorkspaceTreeNode> Build(string workspaceRoot, IEnumerable<string> markdownPaths)
    {
        var root = WorkspaceService.NormalizeRootPath(workspaceRoot);
        var tree = new MutableFolder(string.Empty, string.Empty);
        foreach (var markdownPath in markdownPaths)
        {
            var fullPath = Path.GetFullPath(markdownPath);
            var relativePath = Path.GetRelativePath(root, fullPath);
            if (relativePath == ".." || relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                continue;

            var parts = relativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            var folder = tree;
            var currentPath = string.Empty;
            for (var index = 0; index < parts.Length - 1; index++)
            {
                currentPath = string.IsNullOrEmpty(currentPath) ? parts[index] : Path.Combine(currentPath, parts[index]);
                folder = folder.GetOrAddFolder(parts[index], currentPath);
            }
            folder.Documents.Add(new WorkspaceTreeNode(parts[^1], relativePath, true, []));
        }
        return tree.ToNodes();
    }

    private sealed class MutableFolder(string name, string relativePath)
    {
        private readonly Dictionary<string, MutableFolder> _folders = new(StringComparer.OrdinalIgnoreCase);
        public List<WorkspaceTreeNode> Documents { get; } = [];

        public MutableFolder GetOrAddFolder(string childName, string childPath)
        {
            if (!_folders.TryGetValue(childName, out var folder))
            {
                folder = new MutableFolder(childName, childPath);
                _folders.Add(childName, folder);
            }
            return folder;
        }

        public IReadOnlyList<WorkspaceTreeNode> ToNodes()
        {
            var folders = _folders.Values
                .OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
                .Select(folder => new WorkspaceTreeNode(folder.Name, folder.RelativePath, false, folder.ToNodes()));
            var documents = Documents.OrderBy(document => document.Name, StringComparer.OrdinalIgnoreCase);
            return folders.Concat(documents).ToArray();
        }

        private string Name { get; } = name;
        private string RelativePath { get; } = relativePath;
    }
}

public sealed class WorkspaceService(IWorkspaceStore store)
{
    public Task<WorkspaceRecord> OpenAsync(
        string folderPath,
        DateTimeOffset? openedUtc = null,
        CancellationToken cancellationToken = default)
    {
        var rootPath = NormalizeRootPath(folderPath);
        var displayName = GetDisplayName(rootPath);
        return store.OpenWorkspaceAsync(rootPath, displayName, openedUtc ?? DateTimeOffset.UtcNow, cancellationToken);
    }

    public Task<WorkspaceRecord?> GetActiveAsync(CancellationToken cancellationToken = default) =>
        store.GetActiveWorkspaceAsync(cancellationToken);

    public static string NormalizeRootPath(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath));
    }

    private static string GetDisplayName(string rootPath)
    {
        var name = new DirectoryInfo(rootPath).Name;
        return string.IsNullOrWhiteSpace(name) ? rootPath : name;
    }
}
