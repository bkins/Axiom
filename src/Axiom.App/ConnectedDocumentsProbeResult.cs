namespace Axiom.App;

internal sealed record ConnectedDocumentsProbeResult(string Machine
                                                   , bool PreviewClickFollowed
                                                   , bool HeadingScrolled
                                                   , int IndexedFiles
                                                   , int NativeEditSamples
                                                   , double EditToBufferP95Milliseconds
                                                   , bool MarkdownUnchanged
                                                   , bool CleanStartupRestored);
