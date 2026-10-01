using System.ComponentModel;
using AvaloniaEdit.Document;
using Axiom.Core;

namespace Axiom.App;

internal sealed class DocumentTab : INotifyPropertyChanged
{
    private string _label = string.Empty;

    public DocumentTab(DocumentSession session, int untitledNumber)
    {
        Session = session;
        UntitledNumber = untitledNumber;
        Document = new TextDocument(session.Text);
        Document.Changed += (_, _) => Session.UpdateText(Document.Text);
    }

    public DocumentSession Session { get; }
    public TextDocument Document { get; }
    public int UntitledNumber { get; }
    public int CaretOffset { get; set; }
    public int SelectionStart { get; set; }
    public int SelectionLength { get; set; }
    public double HorizontalOffset { get; set; }
    public double VerticalOffset { get; set; }
    public double PreviewScrollRatio { get; set; }
    public string? PreviewAnchorPrefix { get; set; }
    public int PreviewAnchorRelativeOffset { get; set; }
    public string? LastProtectedText { get; set; }
    public string RecoveryStatus { get; set; } = "Recovery ready";
    public bool IsMissing { get; set; }
    public DocumentStructure? Structure { get; set; }
    public bool HasPendingScrollRestore { get; set; }

    public string Label
    {
        get => _label;
        set
        {
            if (_label == value) return;
            _label = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
