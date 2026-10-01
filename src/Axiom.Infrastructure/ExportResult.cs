namespace Axiom.Infrastructure;

public sealed record ExportResult(string Path, IReadOnlyList<string> Warnings);
