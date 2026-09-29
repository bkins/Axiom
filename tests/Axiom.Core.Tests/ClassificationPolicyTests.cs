using Xunit;

namespace Axiom.Core.Tests;

public sealed class ClassificationPolicyTests
{
    [Fact]
    public void ValuesNormalizeUnicodeAndSetsWithoutChangingDisplayedCasing()
    {
        var value = ClassificationPolicy.Normalize(new(Guid.NewGuid(), ["  Cafe\u0301 ", "CAFÉ", "Decision"], " Note ", [" alternate; name "]));
        Assert.Equal(["Café", "Decision"], value.Tags);
        Assert.Equal("Note", value.Type);
        Assert.Equal("alternate; name", Assert.Single(value.Aliases));
        Assert.True(ClassificationPolicy.Matches("folder/file.md", value, ["café"], "note", "NAME"));
        Assert.False(ClassificationPolicy.Matches("folder/file.md", value, ["café", "missing"], null, null));
        Assert.Throws<ArgumentException>(() => ClassificationPolicy.Normalize(value with { Type = "bad\nvalue" }));
        Assert.Throws<ArgumentException>(() => ClassificationPolicy.Normalize(value with { DocumentId = Guid.Empty }));
    }
}
