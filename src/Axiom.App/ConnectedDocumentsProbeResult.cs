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
                                                   , bool RelationshipImportApplied = false
                                                   , bool ReadingModeRetainedBuffers = false
                                                   , bool ReadingModeRestored = false
                                                   , bool OfflineHelpLoaded = false
                                                   , double WorkspaceCancellationMilliseconds = 0
                                                   , double Workspace1000OpenMilliseconds = 0
                                                   , double Workspace10000OpenMilliseconds = 0
                                                   , double DiscoveryEditP95Milliseconds = 0);
