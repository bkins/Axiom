namespace Axiom.Core;

public sealed record MarkdownReferenceDefinition(string Label
                                              , string Destination
                                              , DocumentSourceRange Source);
