using System.Globalization;
using Markdig.Renderers;
using Markdig.Renderers.Html.Inlines;
using Markdig.Syntax.Inlines;

namespace Axiom.Core;

internal sealed class PreviewLinkRenderer(DocumentStructure document, string token) : LinkInlineRenderer
{
    protected override void Write(HtmlRenderer renderer, LinkInline link)
    {
        var index = -1;
        for (var candidate = 0; candidate < document.Links.Count; candidate++)
            if (document.Links[candidate].Source.Offset == link.Span.Start) { index = candidate; break; }
        if (link.IsImage || index < 0) { base.Write(renderer, link); return; }
        renderer.Write("<a href=\"axiom-link://follow/").Write(System.Net.WebUtility.HtmlEncode(token)).Write("/")
                .Write(index.ToString(CultureInfo.InvariantCulture)).Write("\" title=\"");
        renderer.WriteEscape(link.Url ?? string.Empty);
        renderer.Write("\">");
        renderer.WriteChildren(link);
        renderer.Write("</a>");
    }
}
