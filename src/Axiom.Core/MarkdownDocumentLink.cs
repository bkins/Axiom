namespace Axiom.Core;

public sealed record MarkdownDocumentLink(Guid NodeId
                                       , string Text
                                       , string Destination
                                       , string? ReferenceLabel
                                       , DocumentSourceRange Source
                                       , Guid? HeadingNodeId);
