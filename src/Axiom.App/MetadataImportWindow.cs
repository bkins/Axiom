using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Axiom.Core;

namespace Axiom.App;

internal sealed class MetadataImportWindow : Window
{
    public MetadataImportWindow(IReadOnlyList<MetadataImportProposal> proposals, Func<IReadOnlyList<ClassificationChange>, Task> apply)
    {
        Title = "Review metadata import";
        Width = 760;
        Height = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var rows = new StackPanel { Spacing = 8 };
        foreach (var proposal in proposals)
        {
            var choice = new CheckBox { Name = "Replace" + proposal.DocumentId.ToString("N"), Content = new TextBlock { Text = proposal.Description, TextWrapping = TextWrapping.Wrap }, IsEnabled = proposal.CanReplace };
            choice.IsCheckedChanged += (_, _) => proposal.Replace = choice.IsChecked == true;
            rows.Children.Add(choice);
        }
        var status = new TextBlock { Text = $"{proposals.Count} records. All changes default to Skip. Select each Replace explicitly.", TextWrapping = TextWrapping.Wrap };
        var confirm = new Button { Name = "ApplyImport", Content = "Apply selected replacements" };
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => Close();
        confirm.Click += async (_, _) =>
        {
            confirm.IsEnabled = false;
            cancel.IsEnabled = false;
            try
            {
                var selected = proposals.Where(proposal => proposal.Replace && proposal.Change is not null).Select(proposal => proposal.Change!).ToArray();
                await apply(selected);
                Close();
            }
            catch (Exception exception) { status.Text = exception.Message + " No replacements committed. Cancel and refresh the preview."; }
            finally { confirm.IsEnabled = true; cancel.IsEnabled = true; }
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { confirm, cancel } };
        Content = new DockPanel
        {
            Margin = new Thickness(16)
            ,
            Children = { status, buttons, new ScrollViewer { Content = rows } }
        };
        DockPanel.SetDock(status, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
    }
}
