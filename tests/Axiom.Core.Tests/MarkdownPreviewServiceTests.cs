using Axiom.Core;
using System.Diagnostics;

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
        Assert.Contains("li.task-list-item, li:has(input[type=checkbox]) { list-style: none;", html);
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
    public void EmbedsExistingWorkspaceImageForStringNavigatedPreview()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AxiomPreviewImages-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var document = Path.Combine(root, "Test.md");
            var image = Path.Combine(root, "Screenshot one.png");
            File.WriteAllText(document, "![Screenshot](Screenshot%20one.png)");
            File.WriteAllBytes(image, [1, 2, 3, 4]);

            var html = _service.RenderDocument(File.ReadAllText(document), document, workspaceRoot: root);

            Assert.Contains("src=\"data:image/png;base64,AQIDBA==\"", html);
            Assert.DoesNotContain("src=\"Screenshot%20one.png\"", html);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DoesNotEmbedImageOutsideWorkspace()
    {
        var parent = Path.Combine(Path.GetTempPath(), $"AxiomPreviewImages-{Guid.NewGuid():N}");
        var root = Path.Combine(parent, "workspace");
        Directory.CreateDirectory(root);
        try
        {
            var document = Path.Combine(root, "Test.md");
            var image = Path.Combine(parent, "outside.png");
            File.WriteAllBytes(image, [1, 2, 3, 4]);

            var html = _service.RenderDocument("![Outside](../outside.png)", document, workspaceRoot: root);

            Assert.Contains("src=\"../outside.png\"", html);
            Assert.DoesNotContain("data:image/png", html);
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void HidesOnlyTheCanonicalDocumentMarkerFromPreview()
    {
        var id = Guid.NewGuid();
        var html = _service.RenderDocument($"<!-- axiom:documentId={id:D} -->\n\n# Visible", null);

        Assert.DoesNotContain(id.ToString("D"), html);
        Assert.Contains("<h1>Visible</h1>", html);
    }

    [Fact]
    public void PreviewStylesCoverReadableComponentsAndConstrainOverflowInLightAndDark()
    {
        var html = _service.RenderDocument("# Heading\n\n![wide](wide.png)\n\n---", null);

        Assert.Contains("color-scheme: light dark", html);
        Assert.Contains("@media (prefers-color-scheme: light)", html);
        Assert.Contains("table { display: block; max-width: 100%; overflow-x: auto;", html);
        Assert.Contains("img { display: block; max-width: 100%; height: auto;", html);
        Assert.Contains("script-src 'none'", html);
    }

    [Theory]
    [InlineData(AppearancePreference.System, "system")]
    [InlineData(AppearancePreference.Light, "light")]
    [InlineData(AppearancePreference.Dark, "dark")]
    public void PreviewCarriesTheRequestedAppearance(AppearancePreference preference, string expected)
    {
        var html = _service.RenderDocument("# Preview", null, preference);

        Assert.Contains($"<html data-theme=\"{expected}\">", html);
        Assert.Contains(preference == AppearancePreference.System
            ? "@media (prefers-color-scheme: light)"
            : $"html[data-theme=\"{expected}\"]", html);
    }

    [Fact]
    public void LargeDocumentRenderingRemainsBounded()
    {
        var markdown = string.Join('\n', Enumerable.Range(1, 10_000).Select(index => $"- [ ] Item {index}"));
        var stopwatch = Stopwatch.StartNew();

        var html = _service.RenderDocument(markdown, null);

        stopwatch.Stop();
        Assert.Contains("Item 10000", html);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Rendering took {stopwatch.Elapsed}.");
    }

    [Fact]
    public void EmptyPreviewExplainsTheNextAction()
    {
        var html = _service.RenderDocument(string.Empty, null);

        Assert.Contains("Start typing Markdown to see a preview.", html);
        Assert.Contains("class=\"empty-state\"", html);
    }
}

