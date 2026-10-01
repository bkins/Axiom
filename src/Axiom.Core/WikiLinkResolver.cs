namespace Axiom.Core;

public sealed class WikiLinkResolver
{
    public const string Prefix = "axiom-wiki:";
    private readonly string _root;
    private readonly IReadOnlyDictionary<string, DocumentStructure> _documents;
    private readonly Dictionary<string, List<string>> _names = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, List<string>> _identities = [];
    private readonly bool _complete;
    private readonly IReadOnlyList<DocumentClassification>? _classifications;

    public WikiLinkResolver(string root, IReadOnlyDictionary<string, DocumentStructure> documents
                          , IReadOnlyList<DocumentClassification>? classifications = null, bool complete = true)
    {
        _root = WorkspaceService.NormalizeRootPath(root);
        _documents = documents;
        _complete = complete;
        _classifications = classifications;
        var metadata = (classifications ?? []).ToDictionary(item => item.DocumentId);
        foreach (var document in documents)
        {
            WorkspaceService.GetRelativeDocumentPath(_root, document.Key);
            AddName(Path.GetFileNameWithoutExtension(document.Key), document.Key);
            if (document.Value.Headings.FirstOrDefault() is { } title) AddName(title.Text, document.Key);
            if (DocumentIdCodec.Find(document.Value.SourceText) is not { } identity) continue;
            if (!_identities.TryGetValue(identity, out var paths)) _identities[identity] = paths = [];
            paths.Add(document.Key);
            if (metadata.TryGetValue(identity, out var classification))
                foreach (var alias in classification.Aliases) AddName(alias, document.Key);
        }
    }

    public WikiLinkResolver WithOpenDocuments(IReadOnlyDictionary<string, DocumentStructure> openDocuments)
    {
        var documents = _documents.ToDictionary(pair => pair.Key, pair => pair.Value,
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var open in openDocuments)
            if (documents.ContainsKey(open.Key)) documents[open.Key] = open.Value;
        return new WikiLinkResolver(_root, documents, _classifications, _complete);
    }

    public MarkdownLinkResolution Resolve(string sourcePath, string destination)
    {
        try { WorkspaceService.GetRelativeDocumentPath(_root, sourcePath); }
        catch (ArgumentException) { return new(MarkdownLinkResolutionStatus.OutsideWorkspace, "The source document is outside the active workspace."); }
        if (!destination.StartsWith(Prefix, StringComparison.Ordinal)) return new(MarkdownLinkResolutionStatus.InvalidPath, "A wiki link is required.");
        var decoded = Uri.UnescapeDataString(destination[Prefix.Length..]);
        var separator = decoded.IndexOf('#');
        var name = (separator < 0 ? decoded : decoded[..separator]).Trim();
        var fragment = separator < 0 ? "" : decoded[(separator + 1)..].Trim();
        if (decoded.Any(char.IsControl)) return new(MarkdownLinkResolutionStatus.InvalidPath, "Wiki links cannot contain control characters.");
        List<string> candidates;
        var explicitPath = name.Contains('/') || name.Contains('\\') || name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase);
        if (name.Length == 0) candidates = _documents.ContainsKey(sourcePath) ? [sourcePath] : [];
        else if (name.StartsWith("id:", StringComparison.OrdinalIgnoreCase))
        {
            if (!Guid.TryParse(name[3..], out var identity)) return new(MarkdownLinkResolutionStatus.InvalidPath, "The wiki document identity is invalid.");
            candidates = _identities.GetValueOrDefault(identity) ?? [];
        }
        else if (explicitPath)
        {
            try
            {
                var relative = name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase) ? name : name + ".md";
                var path = WorkspaceMutationPolicy.Resolve(_root, relative);
                candidates = _documents.Keys.Where(candidate => string.Equals(candidate, path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)).ToList();
            }
            catch (ArgumentException) { return new(MarkdownLinkResolutionStatus.InvalidPath, "Use a workspace-relative Markdown path without traversal or URI schemes."); }
        }
        else
        {
            if (name.Contains(':') || name.Contains('?') || name is "." or "..")
                return new(MarkdownLinkResolutionStatus.InvalidPath, "The wiki target name is invalid.");
            candidates = _names.GetValueOrDefault(name) ?? [];
        }
        if (!_complete && !explicitPath && name.Length > 0)
            return new(MarkdownLinkResolutionStatus.Unverified, "Workspace indexing is incomplete; name/identity uniqueness cannot be verified.");
        var unique = candidates.Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToArray();
        if (unique.Length == 0) return new(MarkdownLinkResolutionStatus.MissingDocument, "The wiki target is missing. No document was created.");
        if (unique.Length > 1)
            return new(MarkdownLinkResolutionStatus.Ambiguous, "Several documents match this wiki target. Choose a document explicitly.") { Candidates = unique };
        var target = unique[0];
        var result = new MarkdownLinkResolution(MarkdownLinkResolutionStatus.Resolved, "Wiki document resolved.", target, fragment);
        if (fragment.Length == 0) return result;
        var structure = _documents[target];
        var heading = structure.FindHeading(fragment);
        if (heading is null)
        {
            var matches = structure.Headings.Where(item => item.Text.Equals(fragment, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length > 1) return result with { Status = MarkdownLinkResolutionStatus.Ambiguous, Message = "Several headings match. Use an exact heading anchor." };
            heading = matches.SingleOrDefault();
        }
        return heading is null
            ? result with { Status = MarkdownLinkResolutionStatus.MissingHeading, Message = "The wiki document exists, but its requested heading is missing." }
            : result with { Source = heading.Source, Fragment = heading.Fragment };
    }

    private void AddName(string name, string path)
    {
        if (!_names.TryGetValue(name, out var paths)) _names[name] = paths = [];
        paths.Add(path);
    }
}
