namespace Axiom.Core;

public sealed record RecentDocument(string Path, string? WorkspaceRoot, DateTimeOffset OpenedUtc)
{
    public string Label => System.IO.Path.GetFileName(Path) + " — " + Path;
}
