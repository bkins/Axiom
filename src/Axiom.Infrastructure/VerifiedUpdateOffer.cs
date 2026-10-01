namespace Axiom.Infrastructure;

public sealed record VerifiedUpdateOffer(int FormatVersion
                                        , string Version
                                        , string CommitId
                                        , int SchemaVersion
                                        , string Channel
                                        , string ReleaseNotes
                                        , string PackageUrl
                                        , string PackageSha256);
