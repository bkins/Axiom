namespace Axiom.Core;

public sealed record WorkspaceRenameItem(string Source, string Destination, bool Folder);

public static class WorkspaceRenamePlan
{
    public static IReadOnlyList<WorkspaceRenameItem> Create(string root, IEnumerable<(string Relative, bool Folder)> selection, string template,
        Func<string, bool> exists)
    {
        if (string.IsNullOrWhiteSpace(template) || template.Replace("{name}", "").Replace("{n}", "").IndexOfAny(['{', '}']) >= 0)
            throw new ArgumentException("Use a name template containing only literal text, {name} and {n}.");
        var selected = selection.DistinctBy(item => item.Relative, WorkspacePathBoundary.Comparer).OrderBy(item => item.Relative, WorkspacePathBoundary.Comparer).ToArray();
        selected = selected.Where(item => !selected.Any(parent => parent.Folder && parent != item && WorkspacePathBoundary.Contains(Path.Combine(root, parent.Relative), Path.Combine(root, item.Relative)))).ToArray();
        var result = new List<WorkspaceRenameItem>();
        var targets = new HashSet<string>(WorkspacePathBoundary.Comparer);
        foreach (var item in selected)
        {
            var source = WorkspaceMutationPolicy.Resolve(root, item.Relative, !item.Folder);
            if (!exists(source)) throw new IOException("A selected source is missing.");
            var name = item.Folder ? Path.GetFileName(source) : Path.GetFileNameWithoutExtension(source);
            var replacement = template.Replace("{name}", name).Replace("{n}", (result.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (replacement != Path.GetFileName(replacement)) throw new ArgumentException("Rename accepts names, not paths.");
            var relative = Path.Combine(Path.GetDirectoryName(item.Relative) ?? "", replacement + (item.Folder ? "" : Path.GetExtension(source)));
            var destination = WorkspaceMutationPolicy.Resolve(root, relative, !item.Folder);
            if (!targets.Add(destination) || exists(destination)) throw new IOException("A destination exists, repeats, or is a case-only rename. Change the template; nothing was renamed.");
            result.Add(new(source, destination, item.Folder));
        }
        if (result.Count == 0) throw new ArgumentException("Select files or folders first.");
        return result;
    }
}
