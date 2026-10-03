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
        var headerLiterals = new HashSet<LiteralInline>();
        var firstLine = new System.Text.StringBuilder();
        if (paragraph is not null)
        {
            foreach (var inline in paragraph)
            {
                if (inline is LineBreakInline) break;
                if (inline is LiteralInline literal) { headerLiterals.Add(literal); firstLine.Append(literal.Content.ToString()); }
                else if (inline is ContainerInline container)
                    foreach (var child in container.Descendants<LiteralInline>()) { headerLiterals.Add(child); firstLine.Append(child.Content.ToString()); }
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
        renderer.Write("<aside class=\"markdown-alert markdown-alert-").Write(type).Write("\"><p class=\"markdown-alert-title\">")
            .Write(System.Net.WebUtility.HtmlEncode(title)).WriteLine("</p>");
        var filtered = new CalloutMarkerLiteralRenderer(headerLiterals);
        renderer.ObjectRenderers.Insert(0, filtered);
        try { renderer.WriteChildren(quote); }
        finally { renderer.ObjectRenderers.Remove(filtered); }
        renderer.WriteLine("</aside>");
    }
    [GeneratedRegex(@"^\[!(?<kind>note|info|tip|warning|danger)\](?:[ \t]+(?<title>[^\r\n]*))?(?:\r?\n|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Marker();
}
