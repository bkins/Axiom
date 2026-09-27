using Axiom.Core;

namespace Axiom.Core.Tests;

public sealed class MarkdownEditServiceTests
{
    private readonly MarkdownEditService _service = new();

    [Theory]
    [InlineData(MarkdownEditKind.Bold, "**text**", 2, 4)]
    [InlineData(MarkdownEditKind.Italic, "*text*", 1, 4)]
    [InlineData(MarkdownEditKind.InlineCode, "`text`", 1, 4)]
    public void InlineFormattingWrapsAndPreservesTheSelection(
        MarkdownEditKind kind,
        string expected,
        int expectedStart,
        int expectedLength)
    {
        var result = _service.Apply("text", 0, 4, kind);

        Assert.Equal(expected, result.Text);
        Assert.Equal(expectedStart, result.SelectionStart);
        Assert.Equal(expectedLength, result.SelectionLength);
        Assert.True(result.Changed);
    }

    [Fact]
    public void ReapplyingBoldToItsInnerSelectionRemovesTheDelimiters()
    {
        var result = _service.Apply("before **text** after", 9, 4, MarkdownEditKind.Bold);

        Assert.Equal("before text after", result.Text);
        Assert.Equal(7, result.SelectionStart);
        Assert.Equal(4, result.SelectionLength);
    }

    [Theory]
    [InlineData(MarkdownEditKind.Bold, "**bold text**", 2, 9)]
    [InlineData(MarkdownEditKind.Italic, "*italic text*", 1, 11)]
    [InlineData(MarkdownEditKind.InlineCode, "`code`", 1, 4)]
    public void InlineFormattingInsertsASelectedPlaceholder(
        MarkdownEditKind kind,
        string expected,
        int expectedStart,
        int expectedLength)
    {
        var result = _service.Apply(string.Empty, 0, 0, kind);

        Assert.Equal(expected, result.Text);
        Assert.Equal(expectedStart, result.SelectionStart);
        Assert.Equal(expectedLength, result.SelectionLength);
    }

    [Fact]
    public void LinkUsesSelectedTextAndSelectsTheDestination()
    {
        var result = _service.Apply("Axiom", 0, 5, MarkdownEditKind.Link);

        Assert.Equal("[Axiom](https://)", result.Text);
        Assert.Equal("https://", result.Text.Substring(result.SelectionStart, result.SelectionLength));
    }

    [Fact]
    public void LinkWithoutSelectionSelectsTheLabelPlaceholder()
    {
        var result = _service.Apply(string.Empty, 0, 0, MarkdownEditKind.Link);

        Assert.Equal("[link text](https://)", result.Text);
        Assert.Equal("link text", result.Text.Substring(result.SelectionStart, result.SelectionLength));
    }

    [Theory]
    [InlineData(MarkdownEditKind.Heading1, "# Title")]
    [InlineData(MarkdownEditKind.Heading2, "## Title")]
    [InlineData(MarkdownEditKind.Heading3, "### Title")]
    [InlineData(MarkdownEditKind.Paragraph, "Title")]
    [InlineData(MarkdownEditKind.UnorderedList, "- Title")]
    [InlineData(MarkdownEditKind.OrderedList, "1. Title")]
    [InlineData(MarkdownEditKind.TaskList, "- [ ] Title")]
    public void BlockFormattingReplacesAnExistingBlockMarker(MarkdownEditKind kind, string expected)
    {
        var result = _service.Apply("## Title", 4, 0, kind);

        Assert.Equal(expected, result.Text);
        Assert.Equal(expected != "## Title", result.Changed);
    }

    [Fact]
    public void MultilineOrderedListUsesCompleteSelectedLinesAndPreservesCrLf()
    {
        const string source = "alpha\r\nbravo\r\ncharlie";
        var result = _service.Apply(source, 0, 12, MarkdownEditKind.OrderedList);

        Assert.Equal("1. alpha\r\n2. bravo\r\ncharlie", result.Text);
        Assert.Equal("1. alpha\r\n2. bravo", result.Text.Substring(result.SelectionStart, result.SelectionLength));
    }

    [Fact]
    public void FencedCodeWrapsTheSelectionAsOneEdit()
    {
        const string source = "line one\nline two";
        var result = _service.Apply(source, 0, source.Length, MarkdownEditKind.FencedCode);

        Assert.Equal("```\nline one\nline two\n```", result.Text);
        Assert.Equal("line one\nline two", result.Text.Substring(result.SelectionStart, result.SelectionLength));
    }

    [Fact]
    public void ReapplyingFencedCodeToItsInnerSelectionRemovesTheFence()
    {
        const string source = "```\ncode\n```";
        var result = _service.Apply(source, 4, 4, MarkdownEditKind.FencedCode);

        Assert.Equal("code", result.Text);
        Assert.Equal(0, result.SelectionStart);
        Assert.Equal(4, result.SelectionLength);
    }

    [Fact]
    public void CanonicalIdentityLineCannotBeFormatted()
    {
        var source = DocumentIdCodec.Enroll("# Title", Guid.NewGuid());
        var result = _service.Apply(source, 0, 10, MarkdownEditKind.Bold);

        Assert.Equal(source, result.Text);
        Assert.False(result.Changed);
    }

    [Fact]
    public void ContentBelowTheCanonicalIdentityCanStillBeFormatted()
    {
        var source = DocumentIdCodec.Enroll("Title", Guid.NewGuid());
        var start = source.LastIndexOf("Title", StringComparison.Ordinal);
        var result = _service.Apply(source, start, 5, MarkdownEditKind.Bold);

        Assert.EndsWith("**Title**", result.Text);
        Assert.True(result.Changed);
    }

    [Fact]
    public void InvalidSelectionIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.Apply("text", 3, 2, MarkdownEditKind.Bold));
    }
}
