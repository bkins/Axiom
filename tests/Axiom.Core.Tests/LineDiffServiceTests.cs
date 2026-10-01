using Axiom.Core;

namespace Axiom.Core.Tests;

public sealed class LineDiffServiceTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("same\r\n", "same\r\n")]
    [InlineData("old\nkeep\n", "new\nkeep\n")]
    [InlineData("last", "last\n")]
    [InlineData("one\rtwo\n", "one\r\ntwo\n")]
    [InlineData("", "added")]
    [InlineData("removed", "")]
    public void DiffReconstructsBothExactBuffersIncludingLineTerminators(string previous, string current)
    {
        var result = new LineDiffService().Compare(previous, current);
        Assert.Equal(previous, string.Concat(result.Where(line => line.Kind != LineDiffKind.Added).Select(line => line.Text)));
        Assert.Equal(current, string.Concat(result.Where(line => line.Kind != LineDiffKind.Removed).Select(line => line.Text)));
        Assert.Equal(result.Count(line => line.Kind != LineDiffKind.Added), result.Where(line => line.PreviousLine is not null).Select(line => line.PreviousLine).Distinct().Count());
    }

    [Fact]
    public void LargeSharedPrefixDoesNotConsumeQuadraticBudgetAndLargeChangedBlocksRefuse()
    {
        var prefix = string.Concat(Enumerable.Repeat("same\n", 10000));
        Assert.Equal(10002, new LineDiffService().Compare(prefix + "old", prefix + "new").Count);
        Assert.Throws<IOException>(() => new LineDiffService().Compare(string.Concat(Enumerable.Repeat("old\n", 1500)), string.Concat(Enumerable.Repeat("new\n", 1500))));
    }

    [Fact]
    public void RestorePreservesCurrentEnrollmentWithoutEnrollingOrdinaryMarkdown()
    {
        var currentIdentity = Guid.NewGuid();
        var historicalIdentity = Guid.NewGuid();
        var service = new RevisionRestoreService();
        var enrolled = DocumentIdCodec.Enroll("current", currentIdentity);
        Assert.Equal(currentIdentity, DocumentIdCodec.Find(service.Prepare(enrolled, DocumentIdCodec.Enroll("historical", historicalIdentity))));
        Assert.Equal("historical", service.Prepare("ordinary", $"<!-- axiom:documentId={historicalIdentity:D} -->\nhistorical"));
        Assert.Equal(currentIdentity, DocumentIdCodec.Find(service.Prepare(enrolled, "before enrollment")));
        Assert.EndsWith("before enrollment", service.Prepare(enrolled, "before enrollment"));
        Assert.Equal(enrolled, service.Prepare(enrolled, enrolled));
    }
}
