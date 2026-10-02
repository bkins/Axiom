using System.Globalization;
using System.Text;
using Axiom.Core;
using Axiom.Infrastructure;
using SkiaSharp;

namespace Axiom.App;

/// <summary>Offline Linux PDF layout using the same native graphics library as Avalonia.</summary>
internal sealed class PortablePdfExportRenderer : IStructuredPdfExportRenderer
{
    public Task<PdfRenderResult> RenderAsync(ExportDocument document
                                         , ExportSnapshot snapshot
                                         , PdfPageSize pageSize
                                         , CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (snapshot.Markdown.Length > 1_000_000) throw new IOException("Offline Linux PDF supports up to one million characters. Split the document before exporting.");
        using var layout = new Layout(snapshot, pageSize, cancellationToken);
        return Task.FromResult(layout.Render(document));
    }

    private sealed class Layout : IDisposable
    {
        private const float Margin = 48;
        private readonly ExportSnapshot _snapshot;
        private readonly CancellationToken _token;
        private readonly MemoryStream _output = new();
        private readonly SKDocument _pdf;
        private readonly SKPaint _ink = new() { Color = SKColors.Black, IsAntialias = true };
        private readonly Dictionary<(bool Bold, bool Italic, bool Code, int Rune), SKFont> _fonts = new();
        private readonly List<string> _warnings = [];
        private readonly float _width;
        private readonly float _height;
        private SKCanvas _canvas = null!;
        private float _y;
        private int _pages;
        private long _imageBytes;
        private int _imageCount;

        internal Layout(ExportSnapshot snapshot, PdfPageSize size, CancellationToken token)
        {
            _snapshot = snapshot;
            _token = token;
            _width = size == PdfPageSize.A4 ? 595.276f : 612;
            _height = size == PdfPageSize.A4 ? 841.89f : 792;
            _pdf = SKDocument.CreatePdf(_output) ?? throw new IOException("The offline PDF renderer could not start.");
            _warnings.Add("Linux PDF uses offline document layout. Complex-script shaping, emoji, copied Unicode and browser styling depend on installed fonts; review output before sharing.");
        }

        internal PdfRenderResult Render(ExportDocument document)
        {
            NewPage();
            foreach (var block in document.Blocks) Block(block, 0);
            EndPage();
            _pdf.Close();
            _token.ThrowIfCancellationRequested();
            if (_output.Length > 128 * 1024 * 1024) throw new IOException("PDF output exceeds 128 MiB.");
            return new(_output.ToArray(), _warnings.Distinct().ToArray());
        }

        private void NewPage()
        {
            _token.ThrowIfCancellationRequested();
            if (_pages >= 2048) throw new IOException("PDF export exceeds 2,048 pages. Split the document.");
            if (_pages > 0) EndPage();
            _pages++;
            if (_output.Length > 128 * 1024 * 1024) throw new IOException("PDF output exceeds 128 MiB.");
            _canvas = _pdf.BeginPage(_width, _height);
            _y = Margin;
        }

        private void EndPage()
        {
            using var font = new SKFont(SKTypeface.Default, 9);
            _canvas.DrawText("Page " + _pages, _width - Margin, _height - 24, SKTextAlign.Right, font, _ink);
            _pdf.EndPage();
        }

        private void Ensure(float height)
        {
            _token.ThrowIfCancellationRequested();
            if (_y + height > _height - Margin) NewPage();
        }

        private void Block(ExportBlock block, int depth)
        {
            _token.ThrowIfCancellationRequested();
            var indent = Math.Min(depth * 18, 144);
            if (block.Kind == "definitions") return;
            if (block.Kind == "table") { Table(block, indent); return; }
            if (block.Kind == "list")
            {
                var number = block.Start;
                foreach (var item in block.Children)
                {
                    var marker = block.Ordered ? number++ + ". " : "• ";
                    var first = true;
                    foreach (var child in item.Children)
                    {
                        if (first && child.Runs.Count > 0)
                        {
                            Block(child with { Runs = new[] { new ExportRun(marker) }.Concat(child.Runs).ToArray() }, depth + 1);
                            first = false;
                        }
                        else Block(child, depth + 1);
                    }
                }
                return;
            }
            if (block.Kind == "quote")
            {
                foreach (var child in block.Children) Block(child, depth + 1);
                return;
            }
            var size = block.Kind == "heading" ? Math.Max(12, 25 - block.Level * 2) : block.Kind == "code" ? 10 : 11;
            if (block.Kind == "heading") Ensure(size * 3);
            var runs = block.Runs.Select(run => run with { Bold = run.Bold || block.Kind == "heading", Code = run.Code || block.Kind == "code" }).ToArray();
            var lines = Lines(runs, _width - Margin * 2 - indent, size);
            foreach (var line in lines)
            {
                Ensure(size * 1.5f);
                Draw(line, Margin + indent, _y + size, size);
                _y += size * 1.5f;
            }
            foreach (var run in block.Runs.Where(run => run.Image is not null)) Image(run, indent);
            if (lines.Count > 0) _y += 7;
            foreach (var child in block.Children) Block(child, depth);
        }

        private void Image(ExportRun run, float indent)
        {
            if (++_imageCount > 1024) throw new IOException("PDF export exceeds 1,024 images.");
            var image = ExportImageReader.Read(run.Image!, _snapshot, _warnings);
            if (image is null) return;
            _imageBytes += image.Bytes.Length;
            if (_imageBytes > 64 * 1024 * 1024) throw new IOException("PDF images exceed 64 MiB.");
            using var codec = SKCodec.Create(new MemoryStream(image.Bytes));
            if (codec is null || (long)codec.Info.Width * codec.Info.Height > 16_000_000)
            {
                _warnings.Add("Image cannot be decoded or exceeds 16 million pixels; alt text retained: " + run.Text);
                return;
            }
            using var bitmap = SKBitmap.Decode(codec);
            if (bitmap is null) { _warnings.Add("Image could not be decoded; alt text retained: " + run.Text); return; }
            var scale = Math.Min(1, Math.Min((_width - Margin * 2 - indent) / bitmap.Width, (_height - Margin * 2 - 20) / bitmap.Height));
            var width = bitmap.Width * scale;
            var height = bitmap.Height * scale;
            Ensure(height + 10);
            _canvas.DrawBitmap(bitmap, SKRect.Create(Margin + indent, _y, width, height));
            _y += height + 10;
        }

        private void Table(ExportBlock table, float indent)
        {
            var columns = table.Children.Select(row => row.Children.Count).DefaultIfEmpty(0).Max();
            if (columns == 0) return;
            if (columns > 32) throw new IOException("PDF tables support up to 32 columns. Split the table.");
            var cellWidth = (_width - Margin * 2 - indent) / columns;
            if (ExportDocumentTraversal.Flatten([table]).SelectMany(part => part.Runs).Any(run => run.Image is not null))
                _warnings.Add("PDF table images retain alt text; move images outside the table to embed them.");
            var rows = table.Children.Select(row => Enumerable.Range(0, columns).Select(index =>
                Lines(index < row.Children.Count
                    ? ExportDocumentTraversal.Flatten([row.Children[index]]).SelectMany(part => part.Runs).ToArray()
                    : [], cellWidth - 8, 10)).ToArray()).ToArray();
            var header = rows[0];
            var headerHeight = Math.Max(1, header.Max(cell => cell.Count)) * 15 + 8;
            var repeatHeader = headerHeight < (_height - Margin * 2) / 3;
            if (!repeatHeader) _warnings.Add("Oversized table header is split across pages and cannot repeat.");
            for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
            {
                var row = rows[rowIndex];
                var length = Math.Max(1, row.Max(cell => cell.Count));
                for (var lineIndex = 0; lineIndex < length;)
                {
                    if (_y + 23 > _height - Margin)
                    {
                        NewPage();
                        if (rowIndex > 0 && repeatHeader) DrawTableSlice(header, 0, (int)((headerHeight - 8) / 15), Margin + indent, cellWidth);
                    }
                    var count = Math.Min(length - lineIndex, Math.Max(1, (int)((_height - Margin - _y - 8) / 15)));
                    DrawTableSlice(row, lineIndex, count, Margin + indent, cellWidth);
                    lineIndex += count;
                }
            }
            _y += 10;
        }

        private void DrawTableSlice(List<List<ExportRun>>[] cells, int start, int count, float x, float cellWidth)
        {
            for (var column = 0; column < cells.Length; column++)
            {
                _ink.Style = SKPaintStyle.Stroke;
                _ink.StrokeWidth = .5f;
                _canvas.DrawRect(SKRect.Create(x + column * cellWidth, _y, cellWidth, count * 15 + 8), _ink);
                _ink.Style = SKPaintStyle.Fill;
                for (var line = 0; line < count && start + line < cells[column].Count; line++)
                    Draw(cells[column][start + line], x + column * cellWidth + 4, _y + 14 + line * 15, 10);
            }
            _y += count * 15 + 8;
        }

        private List<List<ExportRun>> Lines(IReadOnlyList<ExportRun> runs, float width, float size)
        {
            var lines = new List<List<ExportRun>>();
            var line = new List<ExportRun>();
            float used = 0;
            foreach (var run in runs)
            {
                var elements = StringInfo.GetTextElementEnumerator(run.Text.Replace("\r\n", "\n").Replace('\r', '\n'));
                while (elements.MoveNext())
                {
                    _token.ThrowIfCancellationRequested();
                    var text = elements.GetTextElement();
                    if (text == "\n") { lines.Add(line); line = []; used = 0; continue; }
                    if (text == "\t") text = "    ";
                    var part = run with { Text = text };
                    var font = Font(part, size);
                    var advance = font.MeasureText(text);
                    if (used + advance > width && line.Count > 0)
                    {
                        var breakAt = run.Code ? -1 : line.FindLastIndex(part => string.IsNullOrWhiteSpace(part.Text));
                        if (breakAt >= 0 && breakAt < line.Count - 1)
                        {
                            lines.Add(line.Take(breakAt + 1).ToList());
                            line = line.Skip(breakAt + 1).ToList();
                            used = line.Sum(part => Font(part, size).MeasureText(part.Text));
                        }
                        else { lines.Add(line); line = []; used = 0; }
                    }
                    line.Add(part);
                    used += advance;
                }
            }
            if (line.Count > 0) lines.Add(line);
            return lines;
        }

        private SKFont Font(ExportRun run, float size)
        {
            var rune = Rune.GetRuneAt(run.Text, 0).Value;
            var key = (run.Bold, run.Italic, run.Code, rune);
            if (!_fonts.TryGetValue(key, out var font))
            {
                if (_fonts.Count >= 4096) throw new IOException("PDF exceeds 4,096 distinct styled characters. Split the document.");
                var style = new SKFontStyle(run.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal, SKFontStyleWidth.Normal,
                    run.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
                using var preferred = SKTypeface.FromFamilyName(run.Code ? "monospace" : "sans-serif", style);
                var face = preferred.ContainsGlyph(rune) ? preferred : SKFontManager.Default.MatchCharacter(rune);
                font = new SKFont(face ?? preferred, size);
                if (face is not null && face != preferred) face.Dispose();
                if (!font.ContainsGlyph(rune)) _warnings.Add($"No installed PDF font supports U+{rune:X}; review the missing glyph.");
                _fonts.Add(key, font);
            }
            font.Size = size;
            return font;
        }

        private void Draw(List<ExportRun> line, float x, float baseline, float size)
        {
            foreach (var run in line)
            {
                var font = Font(run, size);
                _canvas.DrawText(run.Text, x, baseline, SKTextAlign.Left, font, _ink);
                var width = font.MeasureText(run.Text);
                if (run.Link is not null && MarkdownExportService.IsSafeLink(run.Link)
                    && Uri.TryCreate(run.Link, UriKind.Absolute, out _))
                    _canvas.DrawUrlAnnotation(SKRect.Create(x, baseline - size, width, size * 1.5f), run.Link);
                x += width;
            }
        }

        public void Dispose()
        {
            _pdf.Dispose();
            foreach (var font in _fonts.Values) font.Dispose();
            _ink.Dispose();
            _output.Dispose();
        }
    }
}
