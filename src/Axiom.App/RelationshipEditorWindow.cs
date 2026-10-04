using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Axiom.Core;

namespace Axiom.App;

internal sealed class RelationshipEditorWindow : Window
{
    internal bool Committed { get; private set; }
    private bool _applying;
    public RelationshipEditorWindow(Guid sourceId, IReadOnlyList<ClassifiedDocument> targets, DocumentRelationship? existing, Func<RelationshipChange, Task> apply)
    {
        Title = existing is null ? "Add directed relationship" : "Edit directed relationship";
        Width = 620;
        Height = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var target = new ComboBox { Name = "RelationshipTarget", ItemsSource = targets, DisplayMemberBinding = new Avalonia.Data.Binding(nameof(ClassifiedDocument.RelativePath)) };
        target.SelectedItem = existing is null ? targets.FirstOrDefault() : targets.FirstOrDefault(document => document.DocumentId == existing.TargetId);
        var kind = new TextBox { Name = "RelationshipKind", Text = existing?.Kind ?? "Related" };
        var note = new TextBox { Name = "RelationshipNote", Text = existing?.Note ?? "", AcceptsReturn = true, MinHeight = 80, MaxHeight = 160 };
        var status = new TextBlock { Text = "Direction: current document → selected target. Nothing is written to Markdown.", TextWrapping = TextWrapping.Wrap };
        var save = new Button { Name = "ApplyRelationship", Content = "Apply" };
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => Close();
        Closing += (_, args) => args.Cancel = _applying;
        save.Click += async (_, _) =>
        {
            if (_applying) return;
            _applying = true;
            save.IsEnabled = cancel.IsEnabled = target.IsEnabled = kind.IsEnabled = note.IsEnabled = false;
            try
            {
                if (target.SelectedItem is not ClassifiedDocument { DocumentId: { } targetId }) throw new ArgumentException("Select a unique saved target.");
                var value = RelationshipPolicy.Normalize(new(existing?.RelationshipId ?? Guid.NewGuid(), sourceId, targetId, kind.Text ?? "", note.Text));
                await apply(new(value, existing?.Revision ?? 0));
                Committed = true;
                _applying = false;
                Close();
            }
            catch (Exception exception) { status.Text = exception.Message; }
            finally { _applying = false; save.IsEnabled = cancel.IsEnabled = target.IsEnabled = kind.IsEnabled = note.IsEnabled = true; }
        };
        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(16)
              , Spacing = 8
              , Children = { status, FieldHelp.Label("Target document"), target, FieldHelp.Label("Kind"), kind, FieldHelp.Label("Optional note"), note,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, cancel } } }
            }
        };
    }
}
