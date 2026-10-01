using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Axiom.Core;

namespace Axiom.Infrastructure;

internal sealed class WordExportWriter
{
    private readonly MainDocumentPart _main;
    private readonly ExportSnapshot _snapshot;
    private readonly List<string> _warnings;
    private int _numberingId;
    private uint _imageId;
    private readonly Dictionary<string, string> _bookmarks = new(StringComparer.Ordinal);
    private int _bookmarkId;

    private WordExportWriter(MainDocumentPart main, ExportSnapshot snapshot, List<string> warnings)
    {
        _main = main;
        _snapshot = snapshot;
        _warnings = warnings;
        var styles = main.AddNewPart<StyleDefinitionsPart>();
        styles.Styles = new Styles(new Style(new StyleName { Val = "Normal" }) { Type = StyleValues.Paragraph, StyleId = "Normal", Default = true });
        for (var level = 1; level <= 6; level++)
            styles.Styles.Append(new Style(new StyleName { Val = "Heading " + level }
                                         , new BasedOn { Val = "Normal" }
                                         , new StyleParagraphProperties(new KeepNext(), new OutlineLevel { Val = level - 1 })
                                         , new StyleRunProperties(new Bold(), new FontSize { Val = (40 - level * 3).ToString(CultureInfo.InvariantCulture) }))
            { Type = StyleValues.Paragraph, StyleId = "Heading" + level });
        main.AddNewPart<NumberingDefinitionsPart>().Numbering = new Numbering();
    }

    public static byte[] Write(ExportDocument document, ExportSnapshot snapshot, List<string> warnings)
    {
        using var stream = new MemoryStream();
        using (var package = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = package.AddMainDocumentPart();
            main.Document = new Document(new Body());
            var writer = new WordExportWriter(main, snapshot, warnings);
            foreach (var heading in ExportDocumentTraversal.Flatten(document.Blocks).Where(block => block.Fragment is not null)) writer._bookmarks.TryAdd(heading.Fragment!, "AxiomHeading" + writer._bookmarks.Count);
            foreach (var block in document.Blocks) writer.AppendBlock(main.Document.Body!, block);
            main.Document.Save();
        }
        return stream.ToArray();
    }

    private void AppendBlock(OpenXmlCompositeElement target, ExportBlock block, int depth = 0, int? number = null)
    {
        if (block.Kind == "definitions") return;
        if (block.Kind == "list")
        {
            var id = ++_numberingId;
            var definition = new AbstractNum { AbstractNumberId = id };
            for (var level = 0; level <= 8; level++) definition.Append(new Level(
                new StartNumberingValue { Val = block.Start }
              , new NumberingFormat { Val = block.Ordered ? NumberFormatValues.Decimal : NumberFormatValues.Bullet }
              , new LevelText { Val = block.Ordered ? "%" + (level + 1) + "." : "•" }
              , new LevelJustification { Val = LevelJustificationValues.Left }
              , new PreviousParagraphProperties(new Indentation { Left = ((level + 1) * 360).ToString(CultureInfo.InvariantCulture), Hanging = "240" }))
            { LevelIndex = level });
            var numberingRoot = _main.NumberingDefinitionsPart!.Numbering!;
            if (numberingRoot.Elements<NumberingInstance>().FirstOrDefault() is { } firstInstance) numberingRoot.InsertBefore(definition, firstInstance);
            else numberingRoot.Append(definition);
            numberingRoot.Append(new NumberingInstance(new AbstractNumId { Val = id }) { NumberID = id });
            foreach (var item in block.Children)
            {
                var first = true;
                foreach (var child in item.Children)
                {
                    AppendBlock(target, child, depth + 1, first && child.Kind != "list" ? id : null);
                    first = false;
                }
            }
            return;
        }
        if (block.Kind == "table")
        {
            var table = new Table(new TableProperties(new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4 }, new LeftBorder { Val = BorderValues.Single, Size = 4 },
                new BottomBorder { Val = BorderValues.Single, Size = 4 }, new RightBorder { Val = BorderValues.Single, Size = 4 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 }, new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 })));
            table.Append(new TableGrid(Enumerable.Range(0, block.Children.Select(row => row.Children.Count).DefaultIfEmpty(0).Max()).Select(_ => new GridColumn()).ToArray()));
            foreach (var row in block.Children)
            {
                var wordRow = new TableRow();
                if (row.Level == 1) wordRow.Append(new TableRowProperties(new TableHeader()));
                foreach (var cell in row.Children)
                {
                    var wordCell = new TableCell();
                    foreach (var child in cell.Children) AppendBlock(wordCell, child);
                    if (wordCell.LastChild is not Paragraph) wordCell.Append(new Paragraph());
                    wordRow.Append(wordCell);
                }
                table.Append(wordRow);
            }
            target.Append(table);
            return;
        }
        if (block.Kind is "quote" or "item" or "cell")
        {
            foreach (var child in block.Children) AppendBlock(target, child, depth + 1);
            return;
        }
        var properties = new ParagraphProperties();
        if (block.Kind == "heading") properties.Append(new ParagraphStyleId { Val = "Heading" + block.Level });
        if (number is { } numbering) properties.Append(new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = numbering }));
        if (depth > 0) properties.Append(new Indentation { Left = (Math.Min(depth, 8) * 360).ToString(CultureInfo.InvariantCulture) });
        var paragraph = new Paragraph(properties);
        string? bookmark = null;
        if (block.Fragment is { } fragment && _bookmarks.TryGetValue(fragment, out var name))
        {
            bookmark = (++_bookmarkId).ToString(CultureInfo.InvariantCulture);
            paragraph.Append(new BookmarkStart { Id = bookmark, Name = name });
        }
        foreach (var run in block.Runs) AppendRun(paragraph, run);
        if (bookmark is not null) paragraph.Append(new BookmarkEnd { Id = bookmark });
        target.Append(paragraph);
    }

    private void AppendRun(Paragraph paragraph, ExportRun value)
    {
        var properties = new RunProperties();
        if (value.Code) properties.Append(new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas" });
        if (value.Bold) properties.Append(new Bold());
        if (value.Italic) properties.Append(new Italic());
        var run = new Run(properties);
        if (value.Image is { } source && ExportImageReader.Read(source, _snapshot, _warnings) is { } image)
        {
            var part = _main.AddImagePart(image.MediaType);
            using var stream = new MemoryStream(image.Bytes);
            part.FeedData(stream);
            var relationship = _main.GetIdOfPart(part);
            var dimensions = ExportImageDimensions.Read(image.Bytes, image.MediaType);
            var width = Math.Min(dimensions.Width, 600) * 9525L;
            var height = (long)(width * dimensions.Height / (double)dimensions.Width);
            var id = ++_imageId;
            var alt = System.Security.SecurityElement.Escape(value.Text);
            run.Append(new Drawing($$"""
                <w:drawing xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><wp:inline xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"><wp:extent cx="{{width}}" cy="{{height}}"/><wp:docPr id="{{id}}" name="Image {{id}}" descr="{{alt}}"/><a:graphic xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:pic xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:nvPicPr><pic:cNvPr id="{{id}}" name="Image {{id}}"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" r:embed="{{relationship}}"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="{{width}}" cy="{{height}}"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing>
                """));
        }
        else
        {
            var lines = value.Text.Replace("\r\n", "\n").Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                if (index > 0) run.Append(new Break());
                run.Append(new Text(lines[index]) { Space = SpaceProcessingModeValues.Preserve });
            }
        }
        if (value.Link is { } link)
        {
            if (MarkdownExportService.IsSafeLink(link))
            {
                if (link.StartsWith('#'))
                {
                    if (_bookmarks.TryGetValue(Uri.UnescapeDataString(link[1..]), out var bookmark)) paragraph.Append(new Hyperlink(run) { Anchor = bookmark });
                    else { _warnings.Add("Missing heading link: " + link); paragraph.Append(run); }
                    return;
                }
                if (Uri.TryCreate(link, UriKind.RelativeOrAbsolute, out var uri))
                {
                    var relation = _main.AddHyperlinkRelationship(uri, true);
                    paragraph.Append(new Hyperlink(run) { Id = relation.Id });
                    return;
                }
            }
            _warnings.Add("Unsafe or invalid link omitted: " + link);
        }
        paragraph.Append(run);
    }
}
