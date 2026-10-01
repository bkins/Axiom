using System.Net;
using System.Text;
using Axiom.Core;

namespace Axiom.Infrastructure;

internal static class HtmlExportWriter
{
    public static byte[] Write(ExportDocument document, ExportSnapshot snapshot, List<string> warnings, PdfPageSize? pageSize = null)
    {
        var body = new StringBuilder();
        foreach (var block in document.Blocks) WriteBlock(body, block, snapshot, warnings);
        var print = pageSize is null ? string.Empty : "@page{background:white;size:" + (pageSize == PdfPageSize.A4 ? "A4" : "letter") + " portrait;margin:18mm;@bottom-center{content:'Page ' counter(page) ' of ' counter(pages);font:9pt 'Segoe UI',sans-serif;color:#555}}@media print{body{font-size:11pt;line-height:1.45;margin:0;padding:0;max-width:none}table{display:table;table-layout:fixed;width:100%;overflow:visible}thead{display:table-header-group}td,th{overflow-wrap:anywhere;word-break:break-word;padding:5pt}h1,h2,h3,h4,h5,h6{break-after:avoid}pre{white-space:pre-wrap;overflow-wrap:anywhere;word-break:break-all}img{max-height:230mm;object-fit:contain;break-inside:avoid}blockquote{margin-left:0}p{orphans:2;widows:2}}";
        var html = "<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; img-src data:; style-src 'unsafe-inline'\"><style>html{background:white;color-scheme:light}body{font:16px/1.6 'Segoe UI',sans-serif;max-width:900px;margin:auto;padding:24px;color:#202938;background:white;overflow-wrap:anywhere}table{border-collapse:collapse;display:block;overflow:auto}td,th{border:1px solid #aaa;padding:8px}pre{white-space:pre-wrap;background:#f2f4f7;padding:16px}code{font-family:Consolas,monospace}blockquote{border-left:4px solid #889;padding-left:16px}img{max-width:100%;height:auto}" + print + "</style></head><body>" + body + "</body></html>";
        if (html.Length > 64 * 1024 * 1024) throw new IOException("Embedded export content exceeds 64 million characters. Reduce document size or images.");
        return Encoding.UTF8.GetBytes(html);
    }

    private static void WriteBlock(StringBuilder output, ExportBlock block, ExportSnapshot snapshot, List<string> warnings)
    {
        if (block.Kind == "definitions") return;
        if (block.Kind == "table")
        {
            output.Append("<table>");
            foreach (var header in block.Children.Where(row => row.Level == 1))
            {
                output.Append("<thead>");
                WriteBlock(output, header, snapshot, warnings);
                output.Append("</thead>");
            }
            output.Append("<tbody>");
            foreach (var row in block.Children.Where(row => row.Level != 1)) WriteBlock(output, row, snapshot, warnings);
            output.Append("</tbody></table>");
            return;
        }
        var tag = block.Kind switch { "heading" => "h" + block.Level, "code" => "pre", "quote" => "blockquote", "list" => block.Ordered ? "ol" : "ul", "item" => "li", "table" => "table", "row" => "tr", "cell" => "td", _ => "p" };
        output.Append('<').Append(tag);
        if (block.Fragment is { } fragment) output.Append(" id=\"").Append(WebUtility.HtmlEncode(fragment)).Append('"');
        if (block.Kind == "list" && block.Ordered) output.Append(" start=\"").Append(block.Start).Append('"');
        output.Append('>');
        foreach (var run in block.Runs)
        {
            if (run.Image is { } source)
            {
                var image = ExportImageReader.Read(source, snapshot, warnings);
                if (image is not null && output.Length + (long)image.Bytes.Length * 4 / 3 > 64 * 1024 * 1024)
                    throw new IOException("Embedded export images exceed the document resource limit. Reduce images before exporting.");
                if (image is not null) output.Append("<img alt=\"").Append(WebUtility.HtmlEncode(run.Text)).Append("\" src=\"data:").Append(image.MediaType).Append(";base64,").Append(Convert.ToBase64String(image.Bytes)).Append("\">");
                else output.Append(WebUtility.HtmlEncode(run.Text));
                continue;
            }
            var text = WebUtility.HtmlEncode(run.Text).Replace("\n", "<br>");
            if (run.Code) text = "<code>" + text + "</code>";
            if (run.Bold) text = "<strong>" + text + "</strong>";
            if (run.Italic) text = "<em>" + text + "</em>";
            if (run.Link is { } link)
            {
                if (MarkdownExportService.IsSafeLink(link)) text = "<a href=\"" + WebUtility.HtmlEncode(link) + "\">" + text + "</a>";
                else warnings.Add("Unsafe link omitted: " + link);
            }
            output.Append(text);
        }
        foreach (var child in block.Children) WriteBlock(output, child, snapshot, warnings);
        output.Append("</").Append(tag).Append('>');
    }
}
