namespace Axiom.Core;

public sealed record DocumentStructureNode(Guid NodeId
                                        , DocumentStructureNodeKind Kind
                                        , string Text
                                        , DocumentSourceRange Source
                                        , Guid? ParentHeadingId
                                        , bool? Checked = null
                                        , string? Language = null);
