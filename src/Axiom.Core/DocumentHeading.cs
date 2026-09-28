namespace Axiom.Core;

public sealed record DocumentHeading(Guid NodeId
                                  , int Level
                                  , string Text
                                  , string Fragment
                                  , DocumentSourceRange Source);
