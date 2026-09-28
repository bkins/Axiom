using Markdig.Syntax;

namespace Axiom.Core;

public sealed class DocumentStructure
{
    internal DocumentStructure(string sourceText
                             , MarkdownDocument syntax
                             , IEnumerable<DocumentHeading> headings
                             , IEnumerable<MarkdownDocumentLink> links
                             , IEnumerable<MarkdownReferenceDefinition> definitions)
    {
        SourceText = sourceText;
        Syntax = syntax;
        Headings = Array.AsReadOnly(headings.ToArray());
        Links = Array.AsReadOnly(links.ToArray());
        ReferenceDefinitions = Array.AsReadOnly(definitions.ToArray());
    }

    public string SourceText { get; }
    public IReadOnlyList<DocumentHeading> Headings { get; }
    public IReadOnlyList<MarkdownDocumentLink> Links { get; }
    public IReadOnlyList<MarkdownReferenceDefinition> ReferenceDefinitions { get; }
    internal MarkdownDocument Syntax { get; }

    public DocumentHeading? FindHeading(string decodedFragment) =>
        Headings.FirstOrDefault(heading => heading.Fragment.Equals(decodedFragment, StringComparison.Ordinal));

    public MarkdownDocumentLink? FindLinkAt(int offset) =>
        Links.FirstOrDefault(link => link.Source.Contains(offset));
}
