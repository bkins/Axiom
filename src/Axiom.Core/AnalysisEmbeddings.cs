namespace Axiom.Core;

public sealed record AnalysisEmbeddings(float[][] Vectors, string Provider, string Model, string Provenance, IReadOnlyList<AnalysisSnapshot> Sources, string? ModelRevision = null);
