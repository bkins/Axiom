using Avalonia.Media.TextFormatting;
using AvaloniaEdit.Rendering;

namespace Axiom.App;

internal sealed class EndOfLineTextElement() : FormattedTextElement("¶", 0)
{
    public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context)
    {
        TextRunProperties.SetForegroundBrush(context.TextView.NonPrintableCharacterBrush);
        return base.CreateTextRun(startVisualColumn, context);
    }
}
