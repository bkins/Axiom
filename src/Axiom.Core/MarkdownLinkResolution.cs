namespace Axiom.Core;

public sealed record MarkdownLinkResolution(MarkdownLinkResolutionStatus Status
                                         , string Message
                                         , string? TargetPath = null
                                         , string? Fragment = null
                                         , DocumentSourceRange? Source = null);
