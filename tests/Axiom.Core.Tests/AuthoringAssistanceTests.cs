using Axiom.Core;

namespace Axiom.Core.Tests;

public sealed class AuthoringAssistanceTests
{
    [Theory]
    [InlineData("note", true)]
    [InlineData("info", true)]
    [InlineData("tip", true)]
    [InlineData("warning", true)]
    [InlineData("danger", true)]
    [InlineData("unknown", false)]
    public void TitledCalloutsRenderKnownTypesAndPreserveUnknownQuotations(string type, bool styled)
    {
        var source = $"> [!{type}] Title\n> Body\n>\n> > Nested quotation\n\n```text\n> [!note] Literal code\n```";
        var html = new MarkdownPreviewService().RenderDocument(source, null);
        Assert.Equal(styled, html.Contains("<aside class=\"markdown-alert markdown-alert-" + type + "\">", StringComparison.Ordinal));
        Assert.Contains("Nested quotation", html);
        Assert.Contains("&gt; [!note] Literal code", html);
    }
    [Theory]
    [InlineData("hello", 5, "hello\\\n")]
    [InlineData("hello\r\nworld", 5, "hello\\\r\n\r\nworld")]
    [InlineData("```text\nhello\n```", 13, "```text\nhello\n\n```")]
    public void HardBreakPreservesNewlineConventionAndCode(string source, int caret, string expected)
    {
        var result = new MarkdownEditService().Apply(source, caret, 0, MarkdownEditKind.HardBreak);
        Assert.Equal(expected, result.Text);
        Assert.Equal(0, result.SelectionLength);
    }

    [Fact]
    public void IdentityCannotBeReplacedByHardBreakOrTemplates()
    {
        var source = DocumentIdCodec.Enroll("# Content", Guid.NewGuid());
        foreach (var kind in new[] { MarkdownEditKind.HardBreak, MarkdownEditKind.Callout, MarkdownEditKind.Table, MarkdownEditKind.HorizontalRule })
            Assert.False(new MarkdownEditService().Apply(source, 2, 3, kind).Changed);
    }

    [Fact]
    public void PreviewAnnotationsAndCalloutDoNotChangeSourceOrLeakIntoExport()
    {
        var source = DocumentIdCodec.Enroll("> [!note] Title\n> Body **bold**\n\n```mermaid\nflowchart LR\n A --> B\n```", Guid.NewGuid());
        var structure = new MarkdownStructureService().Parse(source);
        var preview = new MarkdownPreviewService().RenderDocument(structure, null, linkToken: "test", showLineNumbers: true);
        Assert.Contains("Go to editor · Line 3", preview);
        Assert.True(preview.Contains("<aside class=\"markdown-alert markdown-alert-note\">", StringComparison.Ordinal), preview[preview.IndexOf("</style>", StringComparison.Ordinal)..]);
        Assert.Contains("language-mermaid", preview);
        Assert.DoesNotContain("documentId", preview);
        Assert.Equal(source, structure.SourceText);
        var export = new MarkdownExportService().Parse(new(source, null));
        Assert.Contains(export.Warnings, warning => warning.Contains("Mermaid"));
        Assert.Equal("quote", export.Blocks[0].Kind);
        Assert.DoesNotContain("Go to editor", string.Join("", export.Blocks.SelectMany(block => block.Runs).Select(run => run.Text)));
    }
}
