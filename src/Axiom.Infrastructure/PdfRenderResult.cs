namespace Axiom.Infrastructure;

public sealed record PdfRenderResult(byte[] Bytes, IReadOnlyList<string> Warnings);
