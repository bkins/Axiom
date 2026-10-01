using Axiom.Core;

namespace Axiom.Core.Tests;

public sealed class ReadingStatisticsTests
{
    [Fact]
    public void SourceWordCountHasExplicitUnicodeAndIdentityRules()
    {
        var text = DocumentIdCodec.Enroll("# Café don't 日本語\n`code 42` [label](https://example.com)", Guid.NewGuid());
        Assert.Equal(9, DocumentStatistics.CountSourceWords(text));
        Assert.Equal(0, DocumentStatistics.CountSourceWords(" "+"😀 --"));
        Assert.Equal("CRLF", DocumentStatistics.DescribeNewlines("a\r\nb\r\n"));
        Assert.Equal("LF", DocumentStatistics.DescribeNewlines("a\nb"));
        Assert.Equal("CR", DocumentStatistics.DescribeNewlines("a\rb"));
        Assert.Equal("Mixed newlines", DocumentStatistics.DescribeNewlines("a\r\nb\nc\r"));
        Assert.Equal("No newline", DocumentStatistics.DescribeNewlines(""));
    }

    [Fact]
    public void PreviewAnchorsUseOriginalSourceOffsetsAndRemainIsolated()
    {
        var text = DocumentIdCodec.Enroll("# Heading\n\nParagraph **bold**\n\n```js\nalert('x')\n```\n\n<script>alert(1)</script>", Guid.NewGuid());
        var structure = new MarkdownStructureService().Parse(text);
        var html = new MarkdownPreviewService().RenderDocument(structure, null, linkToken: "sessiontoken");
        Assert.Contains($"data-source-start=\"{text.IndexOf("# Heading", StringComparison.Ordinal)}\"", html);
        Assert.Contains("axiom-source://show/sessiontoken/", html);
        Assert.Contains("script-src 'none'", html);
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("axiom:documentId=", html);
        Assert.DoesNotContain("axiom-source", new MarkdownPreviewService().RenderDocument(structure, null));
    }
}
