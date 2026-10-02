namespace Axiom.Core;

public sealed record SemanticSection(string DocumentKey, string DocumentHash, int Offset, int Length, int LineNumber, string Heading, AnalysisSnapshot Snapshot);
