namespace Axiom.Core.Tests;

public sealed class AnalysisCitationPolicyTests
{
    [Fact]
    public void Resolves_Only_Existing_Hash_Verified_Bounded_Source_Ranges()
    {
        var source = AnalysisSnapshot.Capture("saved:Café:abc", "# 🌲\r\nFact Friday\r\nOther fact");
        var insight = new AnalysisInsight("Friday [[saved:Café:abc|L2-L3]] [[unknown|L1]] [[saved:Café:abc|L0]] [[saved:Café:abc|L4]] [[saved:Café:abc|L3-L2]]", "Ollama", "fixture", "provider_response_model", DateTimeOffset.UtcNow, [source]);

        var result = Assert.Single(AnalysisCitationPolicy.Resolve(insight));

        Assert.Equal(2, result.LineNumber);
        Assert.Equal("Fact Friday\r\nOther fact", source.Text.Substring(result.Offset, result.Length));
        Assert.Equal(source.SourceHash, result.DocumentHash);
        Assert.Contains(source.SourceHash, AnalysisCitationPolicy.Report(insight));
        Assert.Contains("not whether a claim is true", AnalysisCitationPolicy.Report(insight));
        Assert.Empty(AnalysisCitationPolicy.Resolve(insight with { Sources = [source with { Text = "changed" }] }));
    }

    [Fact]
    public void Missing_And_Duplicate_Citations_Are_Not_Invented()
    {
        var source = AnalysisSnapshot.Capture("one", "Only line");
        var insight = new AnalysisInsight("Uncited answer", "Ollama", null, "unknown", DateTimeOffset.UtcNow, [source]);

        Assert.Empty(AnalysisCitationPolicy.Resolve(insight));
        Assert.Single(AnalysisCitationPolicy.Resolve(insight with { Text = "[[one|L1]] [[one|L1]]" }));
        Assert.Contains("Resolved citation ranges: 0", AnalysisCitationPolicy.Report(insight));
    }
}
