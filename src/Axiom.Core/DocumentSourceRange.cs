namespace Axiom.Core;

/// <summary>UTF-16 source offsets; line and column are one-based.</summary>
public sealed record DocumentSourceRange(int Offset, int Length, int LineNumber, int ColumnNumber)
{
    public int EndOffset => Offset + Length;
    public bool Contains(int offset) => offset >= Offset && offset < EndOffset;
}
