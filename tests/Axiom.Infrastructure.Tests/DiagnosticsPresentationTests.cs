using Axiom.Infrastructure;

namespace Axiom.Infrastructure.Tests;

public sealed class DiagnosticsPresentationTests
{
    [Fact]
    public void LogLockRetainsReportedProblemAndGivesAConcreteNextStep()
    {
        const string raw = """
            {"timestampUtc":"2026-10-03T18:00:00Z","level":"Error","eventName":"save.failed","path":"example.md","error":"The process cannot access the file because it is being used by another process."}
            """;
        var summary = DiagnosticsPresentation.Summarize(raw);
        Assert.Contains("2026-10-03 18:00:00 UTC", summary);
        Assert.Contains("Error · save failed", summary);
        Assert.Contains("Document: example.md", summary);
        Assert.Contains("close other Axiom windows", summary);
        Assert.Contains("Unsaved work should be preserved", summary);
    }

    [Fact]
    public void UnknownAndMalformedRecordsRemainInspectableWithoutInventingACause()
    {
        var summary = DiagnosticsPresentation.Summarize("broken record\n{\"level\":\"Information\",\"eventName\":\"future.feature.event\"}\n[1]");
        Assert.Contains("broken record", summary);
        Assert.Contains("future feature event", summary);
        Assert.Contains("Time unavailable", summary);
        Assert.Contains("[1]", summary);
        Assert.DoesNotContain("Next step:", summary);
    }
}
