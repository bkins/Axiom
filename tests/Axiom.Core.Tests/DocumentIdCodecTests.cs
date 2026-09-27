using Axiom.Core;

namespace Axiom.Core.Tests;

public sealed class DocumentIdCodecTests
{
    [Fact]
    public void ReassignChangesOnlyTheIdentityMarker()
    {
        var original = Guid.NewGuid();
        var replacement = Guid.NewGuid();
        var markdown = DocumentIdCodec.Enroll("# Body", original);

        var reassigned = DocumentIdCodec.Reassign(markdown, replacement);

        Assert.Equal(replacement, DocumentIdCodec.Find(reassigned));
        Assert.Contains("# Body", reassigned);
        Assert.DoesNotContain(original.ToString("D"), reassigned);
    }

    [Fact]
    public void IgnoresMarkersInFencedExamplesAndEmptyPlaceholderMarkers()
    {
        var text = """
            # Document

            ```markdown
            <!-- axiom:documentId=00000000-0000-0000-0000-000000000000 -->
            ```
            """;

        Assert.Null(DocumentIdCodec.Find(text));

        var id = Guid.NewGuid();
        var enrolled = DocumentIdCodec.Enroll(text, id);
        Assert.Equal(id, DocumentIdCodec.Find(enrolled));
        Assert.Contains("<!-- axiom:documentId=00000000-0000-0000-0000-000000000000 -->", enrolled);
    }
}
