namespace Axiom.Core;

public static class WorkspaceDocumentResolver
{
    public static WorkspaceRecord? Resolve(string documentPath
                                         , IEnumerable<WorkspaceRecord> workspaces
                                         , string? activeRoot)
    {
        var path = Path.GetFullPath(documentPath);
        var matches = workspaces.Where(workspace => WorkspacePathBoundary.Contains(workspace.RootPath, path));
        return matches.OrderByDescending(workspace => activeRoot is not null &&
                         WorkspaceService.NormalizeRootPath(workspace.RootPath).Equals(WorkspaceService.NormalizeRootPath(activeRoot), WorkspacePathBoundary.Comparison))
                      .ThenByDescending(workspace => WorkspaceService.NormalizeRootPath(workspace.RootPath).Length)
                      .ThenByDescending(workspace => workspace.LastOpenedUtc)
                      .ThenBy(workspace => workspace.WorkspaceId)
                      .FirstOrDefault();
    }
}
