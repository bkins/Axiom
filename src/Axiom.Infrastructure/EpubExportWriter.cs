using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Axiom.Core;

namespace Axiom.Infrastructure;

/// <summary>Offline EPUB 3 packaging of the captured Markdown export model.</summary>
internal sealed class EpubExportWriter(ExportSnapshot snapshot, List<string> warnings)
{
    private static readonly XNamespace Html = "http://www.w3.org/1999/xhtml";
    private static readonly XNamespace Publication = "http://www.idpf.org/2007/opf";
    private static readonly XNamespace Dublin = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Epub = "http://www.idpf.org/2007/ops";
    private readonly Dictionary<string, ExportImage> _images = new(StringComparer.Ordinal);
    private readonly HashSet<string> _fragments = new(StringComparer.Ordinal);
    private long _imageBytes;

    public byte[] Write(ExportDocument document)
    {
        if (snapshot.Markdown.Length > 4 * 1024 * 1024) throw new IOException("Export input exceeds four million characters.");
        var headings = ExportDocumentTraversal.Flatten(document.Blocks).Where(block => block.Kind == "heading").ToArray();
        foreach (var heading in headings.Where(block => block.Fragment is not null)) _fragments.Add(heading.Fragment!);
        var title = string.Concat(headings.FirstOrDefault()?.Runs.Select(run => run.Text) ?? ["Axiom document"]);
        if (string.IsNullOrWhiteSpace(title)) title = "Axiom document";
        var content = Page(title, new XElement(Html + "body", document.Blocks.Where(block => block.Kind != "definitions").Select(Block)));
        var links = headings.Where(block => block.Fragment is not null).Select(heading =>
            new XElement(Html + "li", new XElement(Html + "a"
                , new XAttribute("href", "content.xhtml#" + Uri.EscapeDataString(heading.Fragment!))
                , string.Concat(heading.Runs.Select(run => run.Text))))).ToArray();
        if (links.Length == 0) links = [new XElement(Html + "li", new XElement(Html + "a", new XAttribute("href", "content.xhtml"), title))];
        var navigation = Page("Contents", new XElement(Html + "body", new XElement(Html + "nav"
            , new XAttribute(Epub + "type", "toc"), new XAttribute("id", "toc")
            , new XElement(Html + "h1", "Contents"), new XElement(Html + "ol", links))));
        var manifest = new XElement(Publication + "manifest"
            , Item("content", "content.xhtml", "application/xhtml+xml")
            , Item("navigation", "nav.xhtml", "application/xhtml+xml", "nav")
            , Item("style", "style.css", "text/css"));
        var imageIndex = 0;
        foreach (var image in _images) manifest.Add(Item("image" + ++imageIndex, image.Key, image.Value.MediaType));
        var package = new XDocument(new XElement(Publication + "package"
            , new XAttribute("version", "3.0"), new XAttribute("unique-identifier", "publication-id")
            , new XElement(Publication + "metadata", new XAttribute(XNamespace.Xmlns + "dc", Dublin)
                , new XElement(Dublin + "identifier", new XAttribute("id", "publication-id"), "urn:uuid:" + Guid.NewGuid().ToString("D"))
                , new XElement(Dublin + "title", title), new XElement(Dublin + "language", "und")
                , new XElement(Publication + "meta", new XAttribute("property", "dcterms:modified"), DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")))
            , manifest, new XElement(Publication + "spine", new XElement(Publication + "itemref", new XAttribute("idref", "content")))));
        var container = new XDocument(new XElement(XName.Get("container", "urn:oasis:names:tc:opendocument:xmlns:container")
            , new XAttribute("version", "1.0")
            , new XElement(XName.Get("rootfiles", "urn:oasis:names:tc:opendocument:xmlns:container")
                , new XElement(XName.Get("rootfile", "urn:oasis:names:tc:opendocument:xmlns:container")
                    , new XAttribute("full-path", "EPUB/package.opf"), new XAttribute("media-type", "application/oebps-package+xml")))));
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, "mimetype", Encoding.ASCII.GetBytes("application/epub+zip"), CompressionLevel.NoCompression);
            Add(archive, "META-INF/container.xml", Xml(container));
            Add(archive, "EPUB/package.opf", Xml(package));
            Add(archive, "EPUB/content.xhtml", Xml(content));
            Add(archive, "EPUB/nav.xhtml", Xml(navigation));
            Add(archive, "EPUB/style.css", Encoding.UTF8.GetBytes("body{line-height:1.5}img{max-width:100%;height:auto}table{border-collapse:collapse}td,th{border:1px solid;padding:.4em}pre{white-space:pre-wrap}blockquote{border-left:solid .2em;padding-left:1em}"));
            foreach (var image in _images) Add(archive, "EPUB/" + image.Key, image.Value.Bytes);
        }
        if (stream.Length > 128 * 1024 * 1024) throw new IOException("EPUB output exceeds 128 MiB.");
        return stream.ToArray();
    }

    private XElement Block(ExportBlock block)
    {
        if (block.Kind == "table")
        {
            var table = new XElement(Html + "table");
            var headers = block.Children.Where(row => row.Level == 1).Select(row => Row(row, true)).ToArray();
            if (headers.Length > 0) table.Add(new XElement(Html + "thead", headers));
            table.Add(new XElement(Html + "tbody", block.Children.Where(row => row.Level != 1).Select(row => Row(row, false))));
            return table;
        }
        var tag = block.Kind switch
        {
            "heading" => "h" + block.Level, "code" => "pre", "quote" => "blockquote"
          , "list" => block.Ordered ? "ol" : "ul", "item" => "li", "cell" => "td", _ => "p"
        };
        var element = new XElement(Html + tag);
        if (block.Fragment is not null) element.Add(new XAttribute("id", block.Fragment));
        if (block.Kind == "list" && block.Ordered) element.Add(new XAttribute("start", block.Start));
        foreach (var run in block.Runs) element.Add(Run(run));
        foreach (var child in block.Children.Where(child => child.Kind != "definitions")) element.Add(Block(child));
        return element;
    }

    private XElement Row(ExportBlock row, bool header) => new(Html + "tr", row.Children.Select(cell =>
        new XElement(Html + (header ? "th" : "td"), cell.Children.Select(Block))));

    private object Run(ExportRun run)
    {
        if (run.Image is not null)
        {
            var image = ExportImageReader.Read(run.Image, snapshot, warnings);
            if (image is null) return run.Text;
            if (image.MediaType == "image/bmp") { warnings.Add("EPUB export omits BMP images; alt text retained."); return run.Text; }
            var extension = image.MediaType switch { "image/png" => ".png", "image/jpeg" => ".jpg", _ => ".gif" };
            var name = "images/" + Convert.ToHexString(SHA256.HashData(image.Bytes)).ToLowerInvariant() + extension;
            if (_images.TryAdd(name, image)) _imageBytes += image.Bytes.Length;
            if (_imageBytes > 64 * 1024 * 1024 || _images.Count > 1024) throw new IOException("EPUB supports up to 64 MiB and 1,024 unique images.");
            return new XElement(Html + "img", new XAttribute("src", name), new XAttribute("alt", run.Text));
        }
        object text = run.Code ? new XText(run.Text) : run.Text.Split('\n').SelectMany((line, index) =>
            index == 0 ? new object[] { new XText(line) } : [new XElement(Html + "br"), new XText(line)]).ToArray();
        if (run.Code) text = new XElement(Html + "code", text);
        if (run.Bold) text = new XElement(Html + "strong", text);
        if (run.Italic) text = new XElement(Html + "em", text);
        if (run.Strike) text = new XElement(Html + "del", text);
        if (run.Link is not null)
        {
            var validFragment = run.Link.StartsWith('#') && _fragments.Contains(Uri.UnescapeDataString(run.Link[1..]));
            var external = Uri.TryCreate(run.Link, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" or "mailto";
            if (MarkdownExportService.IsSafeLink(run.Link) && (validFragment || external))
                text = new XElement(Html + "a", new XAttribute("href", run.Link), text);
            else warnings.Add("EPUB export omitted a local, missing-fragment or unsafe link; label retained.");
        }
        return text;
    }

    private static XDocument Page(string title, XElement body) => new(new XElement(Html + "html"
        , new XAttribute(XNamespace.Xmlns + "epub", Epub), new XAttribute("lang", "und"), new XAttribute(XNamespace.Xml + "lang", "und")
        , new XElement(Html + "head", new XElement(Html + "title", title)
            , new XElement(Html + "link", new XAttribute("rel", "stylesheet"), new XAttribute("href", "style.css"), new XAttribute("type", "text/css")))
        , body));

    private static XElement Item(string id, string href, string type, string? properties = null)
    {
        var item = new XElement(Publication + "item", new XAttribute("id", id), new XAttribute("href", href), new XAttribute("media-type", type));
        if (properties is not null) item.Add(new XAttribute("properties", properties));
        return item;
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
