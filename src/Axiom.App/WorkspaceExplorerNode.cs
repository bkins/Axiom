using System.Collections.ObjectModel;
using System.ComponentModel;
using Axiom.Core;

namespace Axiom.App;

public sealed class WorkspaceExplorerNode : INotifyPropertyChanged
{
    private bool _expanded;
    internal Action<WorkspaceExplorerNode>? Expanded { get; set; }
    public string Name { get; }
    public string RelativePath { get; }
    public bool IsDocument { get; }
    public DocumentOutlineEntry? Heading { get; }
    public bool IsPlaceholder { get; }
    public bool IsPinned { get; internal set; }
    public bool IsGroup { get; internal set; }
    internal bool IsPinAlias { get; set; }
    internal string? SourceFingerprint { get; set; }
    public ObservableCollection<WorkspaceExplorerNode> Children { get; } = [];
    public string Icon => Heading is not null ? $"H{Heading.Level}" : IsPlaceholder ? "…" : IsPinned ? "★" : IsDocument ? "▤" : "▸";
    public string Description => Heading is not null ? $"Heading {Heading.Level}, line {Heading.LineNumber}" : IsPlaceholder ? Name : (IsDocument ? "Markdown document: " : "Folder: ") + RelativePath;
    public bool IsExpanded
    {
        get => _expanded;
        set
        {
            if (_expanded == value) return;
            _expanded = value;
            PropertyChanged?.Invoke(this, new(nameof(IsExpanded)));
            Expanded?.Invoke(this);
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;

    internal WorkspaceExplorerNode(string name, string path, bool document = false, DocumentOutlineEntry? heading = null, bool placeholder = false)
    {
        Name = name;
        RelativePath = path;
        IsDocument = document;
        Heading = heading;
        IsPlaceholder = placeholder;
    }
}
