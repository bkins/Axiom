namespace Axiom.App;

internal sealed record ConnectedDocumentsProbeResult(string Machine
                                                   , bool PreviewClickFollowed
                                                   , bool HeadingScrolled
                                                   , int IndexedFiles
                                                   , int NativeEditSamples
                                                   , double EditToBufferP95Milliseconds
                                                   , bool MarkdownUnchanged
                                                   , bool CleanStartupRestored
                                                   , bool MetadataPanelApplied = false
                                                   , bool MetadataImportApplied = false
                                                   , bool RelationshipCreatedAndFollowed = false
                                                   , bool RelationshipImportApplied = false);
