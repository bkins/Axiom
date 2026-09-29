namespace Axiom.App;

internal sealed record LinkIndexBenchmarkResult(int FileCount
                                             , int References
                                             , int Errors
                                             , double BuildMilliseconds
                                             , double PersistenceMilliseconds
                                             , double CancellationAcknowledgementMilliseconds
                                             , bool MarkdownUnchanged);
