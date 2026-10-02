namespace Axiom.Core;

public static class WorkspaceMutationPolicy
{
    public static string MoveDestination(string root, string sourceRelativePath, string selectedFolder)
    {
        var source = Resolve(root, sourceRelativePath);
        var normalizedRoot = WorkspaceService.NormalizeRootPath(root);
        if (!WorkspacePathBoundary.Contains(normalizedRoot, selectedFolder, includeRoot: true))
            throw new ArgumentException("Choose a destination folder inside the active workspace.");
        var folderRelative = Path.GetRelativePath(normalizedRoot, Path.GetFullPath(selectedFolder));
        if (Path.IsPathRooted(folderRelative) || folderRelative == ".." ||
            folderRelative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Choose a destination folder inside the active workspace.");
        var destination = Path.Combine(Path.GetFullPath(selectedFolder), Path.GetFileName(source));
        var relative = Path.GetRelativePath(normalizedRoot, destination);
        Resolve(root, relative);
        return relative;
    }

    public static string Resolve(string root, string relativePath, bool document = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var normalizedRoot = WorkspaceService.NormalizeRootPath(root);
        if (Path.IsPathRooted(relativePath)) throw new ArgumentException("Choose a workspace-relative path.");
        var parts = relativePath.Replace('/', '\\').Split('\\');
        foreach (var part in parts)
        {
            var stem = part.Split('.')[0];
            if (string.IsNullOrWhiteSpace(part) || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
                part.Any(character => character < 32 || "<>:\"|?*".Contains(character)) ||
                new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }.Contains(stem, StringComparer.OrdinalIgnoreCase) ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && "123456789¹²³".Contains(stem[3])))
                throw new ArgumentException("Choose a valid Windows file or folder name.");
        }
        var fullPath = Path.GetFullPath(Path.Combine(normalizedRoot, Path.Combine(parts)));
        if (!WorkspacePathBoundary.Contains(normalizedRoot, fullPath))
            throw new ArgumentException("The target must remain inside its workspace.");
        if (document && !fullPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && !fullPath.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a .md or .markdown document.");
        return fullPath;
    }
}
