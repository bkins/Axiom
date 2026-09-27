using System.Net;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;

namespace Axiom.Core;

public sealed class MarkdownPreviewService
{
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseTaskLists()
        .DisableHtml()
        .Build();

    public string RenderDocument(string markdown, string? documentPath)
    {
        // Identity is source metadata: preserve it in Markdown/editor but not in preview.
        // Fenced examples are unaffected because the marker must be on the first line.
        var body = Markdown.ToHtml(DocumentIdCodec.RemoveCanonicalMarkerForPreview(markdown), _pipeline);
        var baseElement = BuildBaseElement(documentPath);
        return $$"""
            <!doctype html>
            <html><head><meta charset="utf-8">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src file: data:; style-src 'unsafe-inline'; font-src 'none'; media-src 'none'; connect-src 'none'; frame-src 'none'; object-src 'none'; script-src 'none'">
            {{baseElement}}
            <style>
            :root { color-scheme: dark; }
            body { font-family: Segoe UI, sans-serif; line-height: 1.6; max-width: 920px; margin: 0 auto; padding: 28px 34px; color: #e5e7eb; background: #111827; }
            h1,h2,h3,h4 { line-height: 1.25; margin-top: 1.4em; } img { max-width: 100%; height: auto; }
            pre { padding: 14px; overflow: auto; background: #1f2937; border-radius: 6px; }
            code { font-family: Cascadia Mono, Consolas, monospace; } blockquote { border-left: 4px solid #8b5cf6; margin-left: 0; padding-left: 16px; color: #cbd5e1; }
            table { border-collapse: collapse; } th,td { border: 1px solid #4b5563; padding: 6px 10px; } a { color:#93c5fd; } input[type=checkbox] { margin-right: .45em; }
            </style></head><body>{{body}}</body></html>
            """;
    }

    private static string BuildBaseElement(string? documentPath)
    {
        if (string.IsNullOrWhiteSpace(documentPath)) return string.Empty;
        var directory = Path.GetDirectoryName(Path.GetFullPath(documentPath));
        if (directory is null) return string.Empty;
        var uri = new Uri(directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar).AbsoluteUri;
        return $"<base href=\"{WebUtility.HtmlEncode(uri)}\">";
    }
}
