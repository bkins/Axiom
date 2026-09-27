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
            :root {
              color-scheme: light dark;
              --page: #111827; --text: #e5e7eb; --muted: #a8b3c4; --surface: #1f2937;
              --border: #4b5563; --accent: #93c5fd; --quote: #a78bfa; --heading-rule: #334155;
            }
            @media (prefers-color-scheme: light) {
              :root {
                --page: #ffffff; --text: #1f2937; --muted: #526071; --surface: #f3f4f6;
                --border: #cbd5e1; --accent: #1d4ed8; --quote: #6d28d9; --heading-rule: #e2e8f0;
              }
            }
            * { box-sizing: border-box; }
            html { background: var(--page); }
            body { font-family: "Segoe UI", system-ui, sans-serif; font-size: 16px; line-height: 1.65; max-width: 920px; margin: 0 auto; padding: 30px 36px 64px; color: var(--text); background: var(--page); overflow-wrap: anywhere; }
            h1,h2,h3,h4,h5,h6 { line-height: 1.25; margin: 1.55em 0 .55em; scroll-margin-top: 1rem; }
            h1 { font-size: 2em; padding-bottom: .28em; border-bottom: 1px solid var(--heading-rule); }
            h2 { font-size: 1.5em; padding-bottom: .2em; border-bottom: 1px solid var(--heading-rule); }
            h3 { font-size: 1.24em; }
            p,ul,ol,blockquote,pre,table { margin-top: 0; margin-bottom: 1em; }
            li + li { margin-top: .22em; }
            a { color: var(--accent); text-underline-offset: .16em; }
            a:hover { text-decoration-thickness: 2px; }
            code { font-family: "Cascadia Mono", Consolas, monospace; font-size: .92em; padding: .12em .32em; background: var(--surface); border-radius: 4px; }
            pre { padding: 15px 17px; overflow: auto; background: var(--surface); border: 1px solid var(--border); border-radius: 7px; }
            pre code { padding: 0; background: transparent; border-radius: 0; }
            blockquote { border-left: 4px solid var(--quote); margin-left: 0; padding: .2em 0 .2em 1em; color: var(--muted); }
            blockquote > :last-child { margin-bottom: 0; }
            table { display: block; max-width: 100%; overflow-x: auto; border-collapse: collapse; }
            th,td { border: 1px solid var(--border); padding: .45em .7em; text-align: left; }
            th { background: var(--surface); }
            input[type=checkbox] { margin: 0 .5em 0 0; accent-color: var(--quote); }
            li:has(> input[type=checkbox]) { list-style: none; margin-left: -1.25em; }
            hr { height: 1px; margin: 1.8em 0; border: 0; background: var(--border); }
            img { display: block; max-width: 100%; height: auto; object-fit: contain; border-radius: 4px; }
            @media (max-width: 640px) { body { padding: 22px 20px 48px; } }
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
