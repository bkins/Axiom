namespace Axiom.Core;

/// <summary>Lexical containment for filesystem operations; this does not resolve links or define state identity.</summary>
public static class WorkspacePathBoundary
{
    // Preserve Windows behavior; other targets conservatively require the exact root spelling.
    public static StringComparison Comparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public static bool Contains(string root, string path, bool includeRoot = false)
    {
        var normalizedRoot = WorkspaceService.NormalizeRootPath(root);
        var normalizedPath = Path.GetFullPath(path);
        if (string.Equals(normalizedRoot, normalizedPath, Comparison)) return includeRoot;
        var prefix = Path.EndsInDirectorySeparator(normalizedRoot)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(prefix, Comparison);
    }
}
