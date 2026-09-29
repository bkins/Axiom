namespace Axiom.App;

internal sealed record RecoveryProbeBuffer(string DraftKey
                                        , string? FilePath
                                        , string SavedText
                                        , int Sequence
                                        , long ElapsedMilliseconds);
