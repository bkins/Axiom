namespace Axiom.Core;

public static class WorkspaceMutationPolicy
{
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
        var rootPrefix = Path.EndsInDirectorySeparator(normalizedRoot) ? normalizedRoot : normalizedRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The target must remain inside its workspace.");
        if (document && !fullPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && !fullPath.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a .md or .markdown document.");
        return fullPath;
    }
}
