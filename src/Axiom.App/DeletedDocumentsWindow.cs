using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

internal sealed class DeletedDocumentsWindow : Window
{
    internal DeletedDocumentsWindow(IReadOnlyList<DeletedDocument> items)
    {
        Title = "Deleted document recovery";
        Width = 950;
        Height = 650;
        MinWidth = 520;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var list = new ListBox { ItemsSource = items };
        list.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<DeletedDocument>((item, _) => new TextBlock
        {
            Text = item is null ? "" : $"{item.CapturedUtc.LocalDateTime:g} — {item.Status}\n{Path.GetRelativePath(item.Intent.RootPath, item.Intent.SourcePath!)}"
          , TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        var preview = new TextBox { IsReadOnly = true, AcceptsReturn = true };
        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var restore = new Button { Content = "Restore selected…", IsEnabled = false };
        var close = new Button { Content = "Close", IsCancel = true };
        restore.Click += (_, _) => Close(list.SelectedItem as DeletedDocument);
        close.Click += (_, _) => Close(null);
        list.SelectionChanged += (_, _) =>
        {
            restore.IsEnabled = false;
            if (list.SelectedItem is not DeletedDocument item) return;
            try
            {
                if (WorkspaceFileOperations.Hash(item.Content) != item.Intent.ExpectedHash) throw new IOException("Recovery integrity check failed.");
                preview.Text = MarkdownIdentityBytes.Decode(item.Content);
                status.Text = $"{item.Content.Length:N0} bytes; identity {item.Intent.DocumentId?.ToString() ?? "ordinary Markdown"}.\nRecorded metadata: {item.MetadataSnapshot}";
                restore.IsEnabled = item.Status == "Available";
            }
            catch (Exception exception) { preview.Text = ""; status.Text = exception.Message; }
        };
        var notice = new TextBlock
        {
            Text = "Axiom captures documents before its own recycle operation. These copies remain in local Axiom state without automatic expiration. Windows Recycle Bin is separate. External deletions are not captured. Restore never overwrites an existing file; current classifications and relationships remain authoritative."
          , TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        var grid = new Grid { Margin = new Thickness(20), RowSpacing = 12, RowDefinitions = new RowDefinitions("Auto,2*,3*,Auto,Auto") };
        grid.Children.Add(notice);
        grid.Children.Add(list);
        grid.Children.Add(preview);
        var metadata = new ScrollViewer { Content = status, MaxHeight = 110 };
        grid.Children.Add(metadata);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { restore, close } };
        grid.Children.Add(buttons);
        Grid.SetRow(list, 1);
        Grid.SetRow(preview, 2);
        Grid.SetRow(metadata, 3);
        Grid.SetRow(buttons, 4);
        Content = grid;
    }
}
