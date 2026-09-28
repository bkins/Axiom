namespace Axiom.Core;

public sealed record MarkdownLinkTarget(MarkdownLinkResolutionStatus Status, string? Text = null, string? Message = null);
