namespace Axiom.Core;

public sealed record AnalysisCapabilities(bool InsightsConfigured, bool EmbeddingsConfigured, bool Simulated, string? InsightsModel, string? EmbeddingsModel, string Availability, string? EmbeddingsRevision = null, string? InsightsRevision = null);
