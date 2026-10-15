using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Axiom.Core;

namespace Axiom.Infrastructure;

internal sealed class OpenDocumentExportWriter(ExportSnapshot snapshot, List<string> warnings)
{
    private const string MediaType = "application/vnd.oasis.opendocument.text";
    private static readonly XNamespace Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private static readonly XNamespace Text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private static readonly XNamespace Style = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
    private static readonly XNamespace Table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private static readonly XNamespace Draw = "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";
    private static readonly XNamespace Svg = "urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0";
    private static readonly XNamespace Formatting = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";
    private static readonly XNamespace Link = "http://www.w3.org/1999/xlink";
    private static readonly XNamespace Manifest = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";
    private readonly Dictionary<string, ExportImage> _images = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _bookmarks = new(StringComparer.Ordinal);
    private int _tableNumber;
    private int _imageNumber;
    private long _imageBytes;

    public byte[] Write(ExportDocument document)
    {
        foreach (var block in ExportDocumentTraversal.Flatten(document.Blocks).Where(block => block.Fragment is not null))
            _bookmarks.TryAdd(block.Fragment!, "Heading" + _bookmarks.Count);
        var content = new XDocument(new XElement(Office + "document-content"
            , new XAttribute(Office + "version", "1.3"), Styles()
            , new XElement(Office + "body", new XElement(Office + "text", document.Blocks.SelectMany(Block)))));
        var manifest = new XDocument(new XElement(Manifest + "manifest", new XAttribute(Manifest + "version", "1.3")
            , Entry("/", MediaType, "1.3"), Entry("content.xml", "text/xml")
            , _images.Select(image => Entry(image.Key, image.Value.MediaType))));
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, "mimetype", Encoding.ASCII.GetBytes(MediaType), CompressionLevel.NoCompression);
            Add(archive, "content.xml", Xml(content));
            Add(archive, "META-INF/manifest.xml", Xml(manifest));
            foreach (var image in _images) Add(archive, image.Key, image.Value.Bytes);
        }
        return stream.ToArray();
    }

    private static XElement Styles()
    {
        var styles = new XElement(Office + "automatic-styles");
        for (var bits = 0; bits < 16; bits++)
        {
            var properties = new XElement(Style + "text-properties");
            if ((bits & 1) != 0) properties.Add(new XAttribute(Formatting + "font-weight", "bold"));
            if ((bits & 2) != 0) properties.Add(new XAttribute(Formatting + "font-style", "italic"));
            if ((bits & 4) != 0) properties.Add(new XAttribute(Formatting + "font-family", "monospace"));
            if ((bits & 8) != 0) properties.Add(new XAttribute(Style + "text-line-through-style", "solid"));
            styles.Add(new XElement(Style + "style", new XAttribute(Style + "name", "Run" + bits), new XAttribute(Style + "family", "text"), properties));
        }
        foreach (var ordered in new[] { false, true })
            styles.Add(new XElement(Text + "list-style", new XAttribute(Style + "name", ordered ? "NumberList" : "BulletList")
                , Enumerable.Range(1, 10).Select(level => ordered
                    ? new XElement(Text + "list-level-style-number", new XAttribute(Text + "level", level), new XAttribute(Style + "num-format", "1"), new XAttribute(Style + "num-suffix", "."))
                    : new XElement(Text + "list-level-style-bullet", new XAttribute(Text + "level", level), new XAttribute(Text + "bullet-char", "•")))));
        styles.Add(new XElement(Style + "style", new XAttribute(Style + "name", "Quote"), new XAttribute(Style + "family", "paragraph")
            , new XElement(Style + "paragraph-properties", new XAttribute(Formatting + "margin-left", "0.4in"))));
        return styles;
    }

    private IEnumerable<XElement> Block(ExportBlock block)
    {
        if (block.Kind == "definitions") yield break;
        if (block.Kind == "quote")
        {
            foreach (var child in block.Children.SelectMany(Block))
            {
                if (child.Name == Text + "p" || child.Name == Text + "h") child.SetAttributeValue(Text + "style-name", "Quote");
                yield return child;
            }
            yield break;
        }
        if (block.Kind == "list")
        {
            var list = new XElement(Text + "list", new XAttribute(Text + "style-name", block.Ordered ? "NumberList" : "BulletList"));
            foreach (var child in block.Children)
            {
                var item = new XElement(Text + "list-item", child.Children.SelectMany(Block));
                if (block.Ordered && list.Elements().Count() == 0) item.Add(new XAttribute(Text + "start-value", Math.Max(1, block.Start)));
                list.Add(item);
            }
            yield return list;
            yield break;
        }
        if (block.Kind == "table")
        {
            var table = new XElement(Table + "table", new XAttribute(Table + "name", "Table" + ++_tableNumber));
            table.Add(new XElement(Table + "table-column", new XAttribute(Table + "number-columns-repeated", Math.Max(1, block.Children.Select(row => row.Children.Count).DefaultIfEmpty(1).Max()))));
            var headers = block.Children.Where(row => row.Level == 1).Select(Row).ToArray();
            if (headers.Length > 0) table.Add(new XElement(Table + "table-header-rows", headers));
            table.Add(block.Children.Where(row => row.Level != 1).Select(Row));
            yield return table;
            yield break;
        }
        var paragraph = new XElement(Text + (block.Kind == "heading" ? "h" : "p"));
        if (block.Kind == "heading") paragraph.Add(new XAttribute(Text + "outline-level", block.Level));
        if (block.Fragment is not null) paragraph.Add(new XElement(Text + "bookmark", new XAttribute(Text + "name", _bookmarks[block.Fragment])));
        foreach (var run in block.Runs) paragraph.Add(Run(run));
        yield return paragraph;
        foreach (var child in block.Children.SelectMany(Block)) yield return child;
    }

    private XElement Row(ExportBlock row) => new(Table + "table-row", row.Children.Select(cell =>
        new XElement(Table + "table-cell", new XAttribute(Office + "value-type", "string"), cell.Children.SelectMany(Block))));

    private object Run(ExportRun run)
    {
        if (run.Image is not null)
        {
            var image = ExportImageReader.Read(run.Image, snapshot, warnings);
            if (image is null) return Whitespace(run.Text);
            var extension = image.MediaType switch { "image/png" => ".png", "image/jpeg" => ".jpg", "image/gif" => ".gif", _ => ".bmp" };
            var name = "Pictures/" + Convert.ToHexString(SHA256.HashData(image.Bytes)).ToLowerInvariant() + extension;
            if (_images.TryAdd(name, image)) _imageBytes += image.Bytes.Length;
            if (_imageBytes > 64 * 1024 * 1024 || _images.Count > 1024) throw new IOException("ODT supports up to 64 MiB and 1,024 unique images.");
            var dimensions = ExportImageDimensions.Read(image.Bytes, image.MediaType);
            var width = Math.Clamp(dimensions.Width / 96.0, 0.1, 6.0);
            var height = width * dimensions.Height / dimensions.Width;
            return new XElement(Draw + "frame", new XAttribute(Draw + "name", "Image" + ++_imageNumber)
                , new XAttribute(Text + "anchor-type", "as-char")
                , new XAttribute(Svg + "width", width.ToString("0.####", CultureInfo.InvariantCulture) + "in")
                , new XAttribute(Svg + "height", height.ToString("0.####", CultureInfo.InvariantCulture) + "in")
                , new XElement(Draw + "image", new XAttribute(Link + "href", name), new XAttribute(Link + "type", "simple")
                    , new XAttribute(Link + "show", "embed"), new XAttribute(Link + "actuate", "onLoad"))
                , new XElement(Svg + "desc", run.Text));
        }
        var bits = (run.Bold ? 1 : 0) | (run.Italic ? 2 : 0) | (run.Code ? 4 : 0) | (run.Strike ? 8 : 0);
        object text = new XElement(Text + "span", new XAttribute(Text + "style-name", "Run" + bits), Whitespace(run.Text));
        if (run.Link is not null)
        {
            string? href = null;
            if (run.Link.StartsWith('#') && _bookmarks.TryGetValue(Uri.UnescapeDataString(run.Link[1..]), out var bookmark)) href = "#" + bookmark;
            else if (Uri.TryCreate(run.Link, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" or "mailto") href = run.Link;
            if (href is not null && MarkdownExportService.IsSafeLink(run.Link))
                text = new XElement(Text + "a", new XAttribute(Link + "href", href), new XAttribute(Link + "type", "simple"), text);
            else warnings.Add("ODT export omitted a local, missing-fragment or unsafe link; label retained.");
        }
        return text;
    }

    private static object[] Whitespace(string value)
    {
        var content = new List<object>();
        var segment = new StringBuilder();
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character is not ' ' and not '\t' and not '\n' and not '\r') { segment.Append(character); continue; }
            if (segment.Length > 0) { content.Add(new XText(segment.ToString())); segment.Clear(); }
            if (character == ' ')
            {
                var count = 1;
                while (index + 1 < value.Length && value[index + 1] == ' ') { count++; index++; }
                var spaces = new XElement(Text + "s");
                if (count > 1) spaces.Add(new XAttribute(Text + "c", count));
                content.Add(spaces);
            }
            else if (character == '\t') content.Add(new XElement(Text + "tab"));
            else if (character == '\n' || index + 1 >= value.Length || value[index + 1] != '\n') content.Add(new XElement(Text + "line-break"));
        }
        if (segment.Length > 0) content.Add(new XText(segment.ToString()));
        return content.ToArray();
    }

    private static XElement Entry(string path, string type, string? version = null)
    {
        var entry = new XElement(Manifest + "file-entry", new XAttribute(Manifest + "full-path", path), new XAttribute(Manifest + "media-type", type));
        if (version is not null) entry.Add(new XAttribute(Manifest + "version", version));
        return entry;
    }

    private static byte[] Xml(XDocument document)
    {
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    private static void Add(ZipArchive archive, string name, byte[] bytes, CompressionLevel compression = CompressionLevel.Optimal)
    {
        using var output = archive.CreateEntry(name, compression).Open();
        output.Write(bytes);
    }
}
