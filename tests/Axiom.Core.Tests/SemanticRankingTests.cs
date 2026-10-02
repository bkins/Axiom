namespace Axiom.Core.Tests;

public sealed class SemanticRankingTests
{
    [Fact]
    public void Sections_Preserve_Source_Ranges_And_Heading_Labels()
    {
        var source = AnalysisSnapshot.Capture("doc", "Intro\n\n# First\nBody 🌲\n\n# Second\nOther\n");

        var sections = SemanticRanking.Sections(new[] { source });

        Assert.Equal(3, sections.Count);
        Assert.Equal("First", sections[1].Heading);
        Assert.All(sections, section => Assert.Equal(source.Text.Substring(section.Offset, section.Length), section.Snapshot.Text));
        Assert.Equal(source.Text, string.Concat(sections.Select(section => section.Snapshot.Text)));
    }

    [Fact]
    public void Cosine_Handles_Zero_And_Extreme_Finite_Vectors_Without_Overflow()
    {
        Assert.Equal(0, SemanticRanking.Cosine(new[] { 0f, 0f }, new[] { 1f, 2f }));
        Assert.Equal(1, SemanticRanking.Cosine(new[] { float.MaxValue, float.MaxValue }, new[] { float.MaxValue, float.MaxValue }), 8);
        Assert.Throws<ArgumentException>(() => SemanticRanking.Cosine(new[] { float.NaN }, new[] { 1f }));
        Assert.Throws<ArgumentException>(() => SemanticRanking.Cosine(new[] { 1f }, new[] { 1f, 2f }));
    }

    [Fact]
    public void Related_Ranking_Excludes_Origin_Document_And_Is_Deterministic()
    {
        var sources = new[] { AnalysisSnapshot.Capture("origin", "# Source\nOne"), AnalysisSnapshot.Capture("other", "# Related\nTwo") };
        var sections = SemanticRanking.Sections(sources);

        var matches = SemanticRanking.Rank(sections, new[] { new[] { 1f, 0f }, new[] { 1f, 0f } }, new[] { 1f, 0f }, "origin");

        Assert.Single(matches);
        Assert.Equal("other", matches[0].Section.DocumentKey);
        Assert.Equal(1, matches[0].Similarity);
    }
}
