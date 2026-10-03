using AvaloniaEdit.Rendering;

namespace Axiom.App;

internal sealed class EndOfLineElementGenerator : VisualLineElementGenerator
{
    internal bool Enabled { get; set; }
    public override int GetFirstInterestedOffset(int startOffset)
    {
        var line = CurrentContext.VisualLine.LastDocumentLine;
        return Enabled && line.DelimiterLength > 0 && startOffset <= line.EndOffset ? line.EndOffset : -1;
    }
    public override VisualLineElement ConstructElement(int offset)
    {
        return new EndOfLineTextElement();
    }
}
