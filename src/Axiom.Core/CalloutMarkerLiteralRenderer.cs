using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax.Inlines;

namespace Axiom.Core;

internal sealed class CalloutMarkerLiteralRenderer(HashSet<LiteralInline> markers) : HtmlObjectRenderer<LiteralInline>
{
    protected override void Write(HtmlRenderer renderer, LiteralInline literal)
    {
        var value = literal.Content.ToString();
        if (!markers.Contains(literal)) renderer.Write(System.Net.WebUtility.HtmlEncode(value));
    }
}
