using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Axiom.Core;

public sealed record ReferenceEdit(int Offset, int Length, string Before, string After);
public sealed record ReferenceRepair(string Text, IReadOnlyList<ReferenceEdit> Edits, IReadOnlyList<string> Warnings);

public static class RelativeReferenceRepair
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UsePreciseSourceLocation().Build();

    public static ReferenceRepair Plan(string text, string oldDocument, string newDocument, Func<string, string> map,
        Func<string, bool> exists)
    {
        var syntax = Markdown.Parse(text, Pipeline);
        var destinations = syntax.Descendants<LinkInline>().Where(link => link.Reference is null && !link.IsAutoLink)
            .Select(link => (link.Url ?? "", link.UrlSpan)).Concat(
                (syntax.GetLinkReferenceDefinitions(false)?.Links.Values.AsEnumerable() ?? [])
                .Select(reference => (reference.Url ?? "", reference.UrlSpan)));
        var edits = new List<ReferenceEdit>();
        var warnings = new List<string>();
        foreach (var (url, span) in destinations.Distinct())
        {
            if (string.IsNullOrWhiteSpace(url) || url[0] is '#' or '/' or '\\' || Uri.TryCreate(url, UriKind.Absolute, out _)) continue;
            var suffixAt = url.IndexOfAny(['#', '?']);
            var path = suffixAt < 0 ? url : url[..suffixAt];
            if (path.Length == 0) continue;
            try
            {
                var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(oldDocument)!, Uri.UnescapeDataString(path).Replace('/', Path.DirectorySeparatorChar)));
                var mapped = map(target);
                if (Path.GetDirectoryName(oldDocument)!.Equals(Path.GetDirectoryName(newDocument), WorkspacePathBoundary.Comparison) && mapped.Equals(target, WorkspacePathBoundary.Comparison)) continue;
                if (!exists(target)) { warnings.Add($"Unresolved reference retained: {url}"); continue; }
                var relative = Path.GetRelativePath(Path.GetDirectoryName(newDocument)!, mapped).Replace(Path.DirectorySeparatorChar, '/');
                if (relative == Uri.UnescapeDataString(path) && oldDocument == newDocument && mapped == target) continue;
                var replacement = string.Join('/', relative.Split('/').Select(segment => Uri.EscapeDataString(segment))) + (suffixAt < 0 ? "" : url[suffixAt..]);
                if (span.Start < 0 || span.Length == 0 || span.End >= text.Length) { warnings.Add($"Unsupported reference retained: {url}"); continue; }
                var before = text.Substring(span.Start, span.Length);
                if (before == replacement) continue;
                edits.Add(new(span.Start, span.Length, before, replacement));
            }
            catch (Exception error) when (error is ArgumentException or UriFormatException or NotSupportedException)
            { warnings.Add($"Unsupported reference retained: {url}"); }
        }
        foreach (var edit in edits.OrderByDescending(edit => edit.Offset)) text = text.Remove(edit.Offset, edit.Length).Insert(edit.Offset, edit.After);
        if (text.Contains("[[", StringComparison.Ordinal)) warnings.Add("Wiki links are retained; review them separately.");
        return new(text, edits, warnings);
    }
}
