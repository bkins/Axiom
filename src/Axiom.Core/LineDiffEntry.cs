namespace Axiom.Core;

public sealed record LineDiffEntry(LineDiffKind Kind, int? PreviousLine, int? CurrentLine, string Text);
