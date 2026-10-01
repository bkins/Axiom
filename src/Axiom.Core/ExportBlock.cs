namespace Axiom.Core;

public sealed record ExportBlock(string Kind
                              , IReadOnlyList<ExportRun> Runs
                              , IReadOnlyList<ExportBlock> Children
                              , int Level = 0
                              , bool Ordered = false
                              , int Start = 1
                              , string? Fragment = null);
