namespace Axiom.Core;

public sealed record AnalysisInsight(string Text, string Provider, string? Model, string Provenance, DateTimeOffset CreatedAt, IReadOnlyList<AnalysisSnapshot> Sources);
