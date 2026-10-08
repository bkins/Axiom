using AvaloniaEdit.Editing;

namespace Axiom.App;

public partial class MainWindow
{
    private bool TryDeleteOrderedItems()
    {
        if (Editor.TextArea.Selection is RectangleSelection) return false;
        var result = _markdownListEditing.DeleteItems(Editor.Text, Editor.SelectionStart, Editor.SelectionLength);
        if (!result.Changed) return false;
        using (Editor.Document.RunUpdate()) Editor.Document.Replace(0, Editor.Document.TextLength, result.Text);
        Editor.Select(result.SelectionStart, result.SelectionLength);
        UpdateChrome("Deleted ordered list items and renumbered following siblings. Undo restores both.");
        return true;
    }
}
