using Axiom.Core;

namespace Axiom.Core.Tests;

public sealed class MarkdownPreviewServiceTests
{
    private readonly MarkdownPreviewService _service = new();

    [Fact]
    public void RendersApprovedBasicFeaturesAndDisablesRawHtml()
    {
        var html = _service.RenderDocument("""
            # Heading
            - [x] done

            | A | B |
            | - | - |
            | 1 | 2 |

            ```csharp
            var x = 1;
            ```

            <script>alert('no')</script>
            """, null);

        Assert.Contains("<h1>Heading</h1>", html);
        Assert.Contains("type=\"checkbox\"", html);
        Assert.Contains("<table>", html);
        Assert.Contains("language-csharp", html);
        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("default-src 'none'", html);
        Assert.Contains("script-src 'none'", html);
    }

    [Fact]
    public void RelativeResourcesUseDocumentDirectory()
    {
        var html = _service.RenderDocument("![image](assets/picture.png)", "C:\\docs\\chapter\\one.md");
        Assert.Contains("<base href=\"file:///C:/docs/chapter/\">", html);
        Assert.Contains("src=\"assets/picture.png\"", html);
    }

    [Fact]
    public void HidesOnlyTheCanonicalDocumentMarkerFromPreview()
    {
        var id = Guid.NewGuid();
        var html = _service.RenderDocument($"<!-- axiom:documentId={id:D} -->\n\n# Visible", null);

        Assert.DoesNotContain(id.ToString("D"), html);
        Assert.Contains("<h1>Visible</h1>", html);
    }
}

