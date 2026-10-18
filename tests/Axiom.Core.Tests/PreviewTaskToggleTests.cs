using Xunit;

namespace Axiom.Core.Tests;

public sealed class PreviewTaskToggleTests
{
    [Fact]
    public void RenderAndToggleTargetOnlyActualTaskMarkersIncludingNestedAndDuplicateLabels()
    {
        var source = "# Tasks\r\n- [ ] Same\r\n  - [X] Same\r\n- `[ ]` code\r\n```md\r\n- [ ] Example\r\n```\r\n";
        var document = new MarkdownStructureService().Parse(source);
        var tasks = PreviewTaskToggle.Read(document);
        Assert.Equal(2, tasks.Count);
        Assert.Equal(source.IndexOf("[ ]", StringComparison.Ordinal) + 1, tasks[0].Offset);
        Assert.False(tasks[0].Checked);
        Assert.True(tasks[1].Checked);
        Assert.True(PreviewTaskToggle.TryToggle(document, source, tasks[0].Offset, false, out var replacement));
        Assert.Equal('x', replacement);
        Assert.False(PreviewTaskToggle.TryToggle(document, source + "new text", tasks[0].Offset, false, out _));
        Assert.False(PreviewTaskToggle.TryToggle(document, source, tasks[0].Offset, true, out _));
        Assert.False(PreviewTaskToggle.TryToggle(document, source, source.LastIndexOf("[ ]", StringComparison.Ordinal) + 1, false, out _));
        var preview = new MarkdownPreviewService().RenderDocument(document, null, linkToken: "render-token");
        Assert.Contains("axiom-task://toggle/render-token/", preview);
        Assert.Contains("role=\"checkbox\" aria-checked=\"true\"", preview);
        Assert.Contains("script-src 'none'", preview);
        Assert.DoesNotContain("axiom-task://", new MarkdownPreviewService().RenderDocument(document, null));
    }
}
