namespace Axiom.App;

internal sealed record RecoveryProbeBufferResult(string DraftKey
                                              , bool IsNewDocument
                                              , int AnnouncedSequence
                                              , long AnnouncedElapsedMilliseconds
                                              , int RecoveredSequence
                                              , long ObservedLossMilliseconds
                                              , bool SavedFileUnchanged);
