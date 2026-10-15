namespace Axiom.Core;

public sealed record ExportRun(string Text
                            , bool Bold = false
                            , bool Italic = false
                            , bool Code = false
                            , string? Link = null
                            , string? Image = null
                            , bool Strike = false);
