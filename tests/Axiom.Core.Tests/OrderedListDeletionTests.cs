namespace Axiom.Core.Tests;

public sealed class OrderedListDeletionTests
{
    [Fact]
    public void DeletingWholeItemPreservesStartNestedNumbersNewlinesAndOtherLists()
    {
        var source = "5. First\r\n6. Remove\r\n7. Last\r\n   1. Child\r\n   2. Child\r\n8. Final\r\n\r\nParagraph\r\n\r\n1. Separate\r\n3. Intentional\r\n";
        var start = source.IndexOf("6. Remove", StringComparison.Ordinal);
        var result = new MarkdownListEditService().DeleteItems(source, start, "6. Remove\r\n".Length);
        Assert.Equal("5. First\r\n6. Last\r\n   1. Child\r\n   2. Child\r\n7. Final\r\n\r\nParagraph\r\n\r\n1. Separate\r\n3. Intentional\r\n", result.Text);
        Assert.Equal(start, result.SelectionStart);
        Assert.True(result.Changed);
    }

    [Fact]
    public void FirstItemDeletionRetainsOriginalStartAndPartialCodeIdentityEditsAreUnchanged()
    {
        var service = new MarkdownListEditService();
        Assert.Equal("4) Last\n", service.DeleteItems("4) First\n5) Last\n", 0, "4) First\n".Length).Text);
        Assert.False(service.DeleteItems("1. First\n2. Last\n", 3, 2).Changed);
        Assert.False(service.DeleteItems("```\n1. Code\n2. Code\n```\n", 4, "1. Code\n".Length).Changed);
        var source = DocumentIdCodec.Enroll("1. First\n2. Last\n", Guid.NewGuid());
        Assert.False(service.DeleteItems(source, 0, source.IndexOf('\n') + 1).Changed);
    }
}
