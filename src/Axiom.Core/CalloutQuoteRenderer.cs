using System.Text.RegularExpressions;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Axiom.Core;

internal sealed partial class CalloutQuoteRenderer : HtmlObjectRenderer<QuoteBlock>
{
    protected override void Write(HtmlRenderer renderer, QuoteBlock quote)
    {
        var paragraph = (quote.FirstOrDefault() as ParagraphBlock)?.Inline;
        var firstLine = new System.Text.StringBuilder();
        if (paragraph is not null)
        {
            foreach (var inline in paragraph)
            {
                if (inline is LineBreakInline) break;
                if (inline is LiteralInline literal) firstLine.Append(literal.Content.ToString());
                else if (inline is ContainerInline container)
                    foreach (var child in container.Descendants<LiteralInline>()) firstLine.Append(child.Content.ToString());
                else if (inline is CodeInline code) firstLine.Append(code.Content);
            }
        }
        var match = Marker().Match(firstLine.ToString());
        if (!match.Success)
        {
            renderer.WriteLine("<blockquote>"); renderer.WriteChildren(quote); renderer.WriteLine("</blockquote>"); return;
        }
        var type = match.Groups["kind"].Value.ToLowerInvariant();
        var title = match.Groups["title"].Value.Trim();
        if (title.Length == 0) title = type;
        renderer.Write("<aside class=\"markdown-alert markdown-alert-").Write(type).WriteLine("\">");
        CalloutTitle.Write(renderer, type, title);
        // Render the body without changing the parsed source tree or renderer type cache.
        var body = paragraph?.FirstChild;
        while (body is not null && body is not LineBreakInline) body = body.NextSibling;
        body = body?.NextSibling;
        if (body is not null)
        {
            renderer.Write("<p>");
            while (body is not null) { renderer.Write(body); body = body.NextSibling; }
            renderer.WriteLine("</p>");
        }
        foreach (var block in quote.Skip(1)) renderer.Write(block);
        renderer.WriteLine("</aside>");
    }
    [GeneratedRegex(@"^\[!(?<kind>note|info|tip|important|warning|caution|danger)\](?:[ \t]+(?<title>[^\r\n]*))?(?:\r?\n|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Marker();
}
