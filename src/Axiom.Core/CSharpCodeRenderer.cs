using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace Axiom.Core;

internal sealed class CSharpCodeRenderer : CodeBlockRenderer
{
    protected override void Write(HtmlRenderer renderer, CodeBlock block)
    {
        if (block is not FencedCodeBlock fence || !CSharpSyntax.IsLanguage(fence.Info)) { base.Write(renderer, block); return; }
        renderer.Write("<pre><code class=\"language-csharp\">").Write(CSharpSyntax.ToHtml(fence.Lines.ToString())).WriteLine("\n</code></pre>");
    }
}
