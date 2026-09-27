using Axiom.Core;

namespace Axiom.Core.Tests;

public sealed class MarkdownListEditServiceTests
{
    private readonly MarkdownListEditService _service = new();

    [Theory]
    [InlineData("- item", "- item\n- ")]
    [InlineData("  * item", "  * item\n  * ")]
    [InlineData("+ item", "+ item\n+ ")]
    public void EnterContinuesUnorderedMarkerAndIndentation(string source, string expected)
    {
        var result = _service.Apply(source, source.Length, 0, MarkdownListEditKind.Continue);

        Assert.Equal(expected, result.Text);
        Assert.Equal(expected.Length, result.SelectionStart);
        Assert.Equal(0, result.SelectionLength);
    }

    [Fact]
    public void EnterIncrementsOnlyTheCurrentOrderedMarkerAndPreservesCrLf()
    {
        const string source = "  9) item\r\n42. unrelated";
        var result = _service.Apply(source, 9, 0, MarkdownListEditKind.Continue);

        Assert.Equal("  9) item\r\n  10) \r\n42. unrelated", result.Text);
        Assert.Equal(17, result.SelectionStart);
    }

    [Theory]
    [InlineData("- [x] done", "- [x] done\n- [ ] ")]
    [InlineData("  * [X] done", "  * [X] done\n  * [ ] ")]
    public void EnterContinuesTaskItemsUnchecked(string source, string expected)
    {
        var result = _service.Apply(source, source.Length, 0, MarkdownListEditKind.Continue);

        Assert.Equal(expected, result.Text);
        Assert.Equal(expected.Length, result.SelectionStart);
    }

    [Theory]
    [InlineData("- ", "")]
    [InlineData("  3. ", "  ")]
    [InlineData("\t- [ ] ", "\t")]
    public void EnterOnEmptyItemRemovesTheMarker(string source, string expected)
    {
        var result = _service.Apply(source, source.Length, 0, MarkdownListEditKind.Continue);

        Assert.Equal(expected, result.Text);
        Assert.Equal(expected.Length, result.SelectionStart);
    }

    [Fact]
    public void EnterAwayFromTheEndOfTheItemUsesNormalEditorBehavior()
    {
        var result = _service.Apply("- item", 3, 0, MarkdownListEditKind.Continue);

        Assert.False(result.Changed);
    }

    [Fact]
    public void IndentAppliesOneTwoSpaceLevelToSelectedContiguousListLines()
    {
        const string source = "- one\n\t* two\n\n- three\nparagraph";
        var selectedLength = source.IndexOf("\nparagraph", StringComparison.Ordinal);
        var result = _service.Apply(source, 0, selectedLength, MarkdownListEditKind.Indent);

        Assert.Equal("  - one\n  \t* two\n  \n  - three\nparagraph", result.Text);
        Assert.Equal("  - one\n  \t* two\n  \n  - three", result.Text.Substring(result.SelectionStart, result.SelectionLength));
    }

    [Fact]
    public void OutdentRemovesOneMixedIndentationLevel()
    {
        const string source = "  - one\r\n\t* two";
        var result = _service.Apply(source, 0, source.Length, MarkdownListEditKind.Outdent);

        Assert.Equal("- one\r\n* two", result.Text);
        Assert.Equal(result.Text, result.Text.Substring(result.SelectionStart, result.SelectionLength));
    }

    [Fact]
    public void SelectionEndingAtTheNextLineStartDoesNotIndentThatLine()
    {
        const string source = "- one\n- two";
        var result = _service.Apply(source, 0, 6, MarkdownListEditKind.Indent);

        Assert.Equal("  - one\n- two", result.Text);
    }

    [Theory]
    [InlineData(MarkdownListEditKind.Indent)]
    [InlineData(MarkdownListEditKind.Outdent)]
    public void NonListTabActionsRemainWithTheEditor(MarkdownListEditKind kind)
    {
        var result = _service.Apply("paragraph", 4, 0, kind);

        Assert.False(result.Changed);
    }

    [Fact]
    public void CanonicalIdentityLineCannotBeIndented()
    {
        var source = DocumentIdCodec.Enroll("- item", Guid.NewGuid());
        var result = _service.Apply(source, 0, source.Length, MarkdownListEditKind.Indent);

        Assert.False(result.Changed);
    }
}
