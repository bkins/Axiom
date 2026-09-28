namespace Axiom.Infrastructure;

public sealed record PortablePackageManifest(int FormatVersion
                                           , string Version
                                           , string InformationalVersion
                                           , string CommitId
                                           , string Branch
                                           , DateTimeOffset BuiltUtc
                                           , int SchemaVersion
                                           , IReadOnlyList<PortablePackageFile> Files);
