using System.ComponentModel;
using AvaloniaEdit.Document;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
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
    public Selection? EditorSelection { get; set; }
    public TextViewPosition CaretPosition { get; set; }
    public string? SelectionSourceText { get; set; }
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
    public string? SuggestedSaveFolder { get; set; }
    public string IdentityIndicator => Session.DocumentId is null ? "▤" : "◇";
    public string Description => $"{Label}\n{(Session.DocumentId is null ? "Ordinary Markdown" : "Axiom document: " + Session.DocumentId)}\n{Session.Path ?? "Unsaved document"}\n{RecoveryStatus}";

    public void RefreshIndicators()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IdentityIndicator)));
    }

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
