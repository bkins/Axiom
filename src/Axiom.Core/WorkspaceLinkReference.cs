namespace Axiom.Core;

public sealed record WorkspaceLinkReference(string SourceRelativePath
                                         , string? TargetRelativePath
                                         , string Destination
                                         , string Context
                                         , DocumentSourceRange Source
                                         , MarkdownLinkResolutionStatus Status
                                         , bool IsUnsaved = false)
{
    public string Provenance => IsUnsaved ? "Unsaved" : "Saved";
    public string DisplayLocation => $"{Provenance} · Line {Source.LineNumber} · {Status}";
}
