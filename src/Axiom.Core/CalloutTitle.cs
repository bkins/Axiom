using System.Net;
using Markdig.Renderers;

namespace Axiom.Core;

internal static class CalloutTitle
{
    internal static void Write(HtmlRenderer renderer, string kind, string title)
    {
        var path = kind.ToLowerInvariant() switch
        {
            "tip" => "M9 18h6m-5 3h4M8 14a6 6 0 1 1 8 0l-1 3H9z",
            "warning" or "caution" => "M12 3 2 21h20zM12 9v5m0 3v1",
            "danger" => "M8 2h8l6 6v8l-6 6H8l-6-6V8zM12 7v6m0 3v1",
            "important" => "M3 3h18v14H9l-6 4zM12 7v4m0 3v1",
            _ => "M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20M12 10v7m0-11v1"
        };
        renderer.Write("<p class=\"markdown-alert-title\"><svg class=\"callout-icon\" aria-hidden=\"true\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><path d=\"")
            .Write(path).Write("\"/></svg><span>").Write(WebUtility.HtmlEncode(title)).WriteLine("</span></p>");
    }
}
