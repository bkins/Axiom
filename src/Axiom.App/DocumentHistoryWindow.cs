using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

internal sealed class DocumentHistoryWindow : Window
{
    internal DocumentHistoryWindow(GitDocumentHistoryService history
                                  , DocumentHistoryLocation location
                                  , IReadOnlyList<DocumentRevision> revisions
                                  , string current
                                  , string saved
                                  , string? external)
    {
        Title = "Document history";
        Width = 1000;
        Height = 700;
        MinWidth = 520;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var lifetime = new CancellationTokenSource();
        var generation = 0;
        HistoricalRestoreRequest? loaded = null;
        var list = new ListBox { ItemsSource = revisions };
        list.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<DocumentRevision>((revision, _) => new TextBlock
        {
            Text         = revision is null ? "" : $"{revision.CommittedUtc.LocalDateTime:g}  {revision.CommitHash[..12]}\n{revision.Subject}\n{revision.Path}"
          , TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        Avalonia.Automation.AutomationProperties.SetName(list, "Git document revisions with commit date and historical path");
        var preview = new TextBox
        {
            IsReadOnly    = true
          , AcceptsReturn = true
          , FontFamily    = new Avalonia.Media.FontFamily("Consolas")
        };
        Avalonia.Automation.AutomationProperties.SetName(preview, "Historical Markdown source");
        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var targets = new List<string> { "Current buffer", "Saved buffer" };
        if (external is not null) targets.Add("External-change snapshot");
        targets.Add("Another revision");
        var target = new ComboBox { ItemsSource = targets
              , SelectedIndex = 0 };
        var other = new ComboBox { ItemsSource = revisions
              , SelectedIndex = revisions.Count > 1 ? 1 : 0
              , IsVisible = false };
        other.ItemTemplate = list.ItemTemplate;
        target.SelectionChanged += (_, _) => other.IsVisible = Equals(target.SelectedItem, "Another revision");
        var compare = new Button { Content = "Compare"
              , IsEnabled = false };
        var restore = new Button { Content = "Restore into buffer…"
              , IsEnabled = false };
        var close = new Button { Content = "Close"
              , IsCancel = true };
        list.SelectionChanged += async (_, _) =>
        {
            var request = ++generation;
            loaded = null;
            compare.IsEnabled = restore.IsEnabled = false;
            if (list.SelectedItem is not DocumentRevision revision) return;
            status.Text = "Reading revision…";
            try
            {
                var text = await history.ReadAsync(location, revision, lifetime.Token);
                if (request != generation || lifetime.IsCancellationRequested) return;
                loaded = new(revision, text);
                preview.Text = text;
                status.Text = "Read-only Git revision. Restore changes only the protected buffer; Save remains separate.";
                compare.IsEnabled = restore.IsEnabled = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { if (request == generation) { preview.Text = ""; status.Text = exception.Message; } }
        };
        compare.Click += async (_, _) =>
        {
            var selected = loaded;
            if (selected is null) return;
            compare.IsEnabled = false;
            try
            {
                var title = target.SelectedItem?.ToString() ?? "Current buffer";
                var comparison = title switch
                {
                    "Saved buffer"             => saved
                  , "External-change snapshot" => external ?? ""
                  , "Another revision"         => await history.ReadAsync(location, (DocumentRevision)other.SelectedItem!, lifetime.Token)
                  , _                          => current
                };
                if (lifetime.IsCancellationRequested) return;
                await new LineDiffWindow(selected.Revision.CommitHash[..12] + " / " + selected.Revision.Path, selected.Text, title, comparison).ShowDialog(this);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { status.Text = exception.Message; }
            finally { compare.IsEnabled = loaded is not null; }
        };
        restore.Click += (_, _) => { if (loaded is not null) Close(loaded); };
        close.Click += (_, _) => Close(null);
        Closed += (_, _) => { lifetime.Cancel(); lifetime.Dispose(); };
        var actions = new WrapPanel
        {
            Orientation = Orientation.Horizontal
          , Children    = { target, compare, restore, close }
        };
        foreach (var control in actions.Children) control.Margin = new Thickness(0, 0, 10, 8);
        var documents = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("320,*")
          , ColumnSpacing     = 12
          , Children          = { list, preview }
        };
        Grid.SetColumn(preview, 1);
        var guidance = new TextBlock
        {
            Text         = $"Repository: {location.RepositoryRoot}\nDocument: {location.RelativePath}. Latest 200 reachable Git revisions, following Git-detected renames. Uncommitted edits are not history; recovery and Undo remain separate."
          , TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto,Auto")
          , RowSpacing     = 12
          , Margin         = new Thickness(20)
          , Children       = { guidance, documents, status, other, actions }
        };
        Grid.SetRow(documents, 1);
        Grid.SetRow(status, 2);
        Grid.SetRow(other, 3);
        Grid.SetRow(actions, 4);
        Content = grid;
        Opened += (_, _) => list.SelectedIndex = revisions.Count > 0 ? 0 : -1;
    }
}
