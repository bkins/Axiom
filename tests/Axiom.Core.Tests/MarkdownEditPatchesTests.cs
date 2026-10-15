using Axiom.Core;
using System.Text.Json;

namespace Axiom.Core.Tests;

public sealed class MarkdownEditPatchesTests
{
    private static string Changes(params (string Old, string New)[] edits)
        => JsonSerializer.Serialize(new { edits = edits.Select(edit => new { oldText = edit.Old, newText = edit.New }) });

    [Fact]
    public void Small_Edits_Preserve_All_Unchanged_Bytes_And_Apply_Against_Original_Positions()
    {
        const string source = "# Title\r\n\r\nteh bird.\r\nKeep `code` exactly.\r\nLast line.\r\n";
        var result = MarkdownEditPatches.Parse(source, Changes(("Last line.", "Final line."), ("teh bird.", "the small bird.")));
        Assert.Equal(source.Replace("teh bird.", "the small bird.").Replace("Last line.", "Final line."), result);
    }

    [Theory]
    [InlineData("bird bird", "bird")]
    [InlineData("bird", "absent")]
    public void Missing_Or_Ambiguous_Replacements_Are_Refused(string source, string old)
        => Assert.Throws<InvalidOperationException>(() => MarkdownEditPatches.Parse(source, Changes((old, "changed"))));

    [Fact]
    public void Overlap_Is_Refused_And_Replacement_Text_Is_Never_Matched_As_Source()
    {
        Assert.Throws<InvalidOperationException>(() => MarkdownEditPatches.Parse("abcdef", Changes(("abc", "X"), ("bcd", "Y"))));
        Assert.Throws<InvalidOperationException>(() => MarkdownEditPatches.Parse("original", Changes(("original", "inserted"), ("inserted", "replaced"))));
    }

    [Fact]
    public void Identity_Removal_Is_Refused()
    {
        const string marker = "<!-- axiom:documentId=60822ad3-c5ce-4781-81ac-dc0ed5986365 -->";
        Assert.Throws<InvalidOperationException>(() => MarkdownEditPatches.Parse(marker + "\n# Source", Changes((marker, ""))));
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("[]")]
    [InlineData("{\"edits\": [{}]}")]
    [InlineData("{\"edits\": [{\"oldText\": 3, \"newText\": null}]}")]
    public void Malformed_Or_Truncated_Response_Is_Refused(string response)
        => Assert.Throws<InvalidOperationException>(() => MarkdownEditPatches.Parse("source", response));

    [Fact]
    public void Empty_Edit_List_Is_No_Change_And_Json_Fence_Is_Accepted()
    {
        Assert.Equal("source", MarkdownEditPatches.Parse("source", "{\"edits\":[]}"));
        Assert.Equal("revised", MarkdownEditPatches.Parse("source", "```json\n" + Changes(("source", "revised")) + "\n```"));
    }
}
