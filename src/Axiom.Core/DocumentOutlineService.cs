namespace Axiom.Core;

public sealed class DocumentOutlineService
{
    private readonly MarkdownStructureService _structure = new();

    public IReadOnlyList<DocumentOutlineEntry> Parse(string markdown) => Parse(_structure.Parse(markdown));

    public IReadOnlyList<DocumentOutlineEntry> Parse(DocumentStructure document) =>
        document.Headings.Select(heading => new DocumentOutlineEntry(heading.Level
                                                                  , heading.Text
                                                                  , heading.Source.Offset
                                                                  , heading.Source.LineNumber))
                .ToArray();
}
