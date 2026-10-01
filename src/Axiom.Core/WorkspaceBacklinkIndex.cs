namespace Axiom.Core;

public sealed record WorkspaceBacklinkIndex(int FormatVersion
                                         , DateTimeOffset BuiltUtc
                                         , IReadOnlyList<WorkspaceIndexedDocument> Documents
                                         , IReadOnlyList<string> Errors)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public WikiLinkResolver? WikiCatalog { get; init; }
}
