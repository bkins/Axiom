namespace Axiom.Core;

public sealed record ExportDocument(IReadOnlyList<ExportBlock> Blocks, IReadOnlyList<string> Warnings);
