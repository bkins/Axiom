using Axiom.Core;

namespace Axiom.Infrastructure;

public static class WorkspaceCreationService
{
    public static string Create(string parent, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('/') || name.Contains('\\'))
            throw new ArgumentException("Enter a workspace name without folders.");
        var root = Path.GetFullPath(parent);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Choose an existing location for the new workspace.");
        var destination = WorkspaceMutationPolicy.Resolve(root, name, document: false);
        WorkspaceFileOperations.ValidateAncestors(root, destination);
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException("That name already exists. Choose another name, or use Open Workspace for an existing folder.");
        // Moving a fresh empty sibling prevents racing creation from adopting an existing folder.
        var temporary = Directory.CreateDirectory(Path.Combine(root, ".axiom-new-" + Guid.NewGuid().ToString("N")));
        try { Directory.Move(temporary.FullName, destination); }
        finally { if (Directory.Exists(temporary.FullName)) Directory.Delete(temporary.FullName); }
        return destination;
    }
}
