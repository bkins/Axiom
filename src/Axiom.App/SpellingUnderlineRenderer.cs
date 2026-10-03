using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Axiom.Core;

namespace Axiom.App;

internal sealed class SpellingUnderlineRenderer : IBackgroundRenderer
{
    public KnownLayer Layer => KnownLayer.Selection;
    internal IReadOnlyList<SpellingIssue> Issues { get; set; } = [];
    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid) return;
        var pen = new Pen(new SolidColorBrush(Color.Parse("#D66060")), 1.5);
        foreach (var issue in Issues)
        {
            if (issue.Offset + issue.Length > textView.Document.TextLength) continue;
            var segment = new SimpleSegment(issue.Offset, issue.Length);
            foreach (var rectangle in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                drawingContext.DrawLine(pen, new Point(rectangle.Left, rectangle.Bottom - 1), new Point(rectangle.Right, rectangle.Bottom - 1));
        }
    }
}
