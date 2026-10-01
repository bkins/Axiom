using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

internal sealed class RecentDocumentsWindow : Window
{
    public RecentDocumentsWindow(SqliteStateStore state, IReadOnlyList<RecentDocument> documents, Action help)
    {
        Title = "Recent documents";
        Width = 720;
        Height = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var list = new ListBox { ItemsSource = documents, DisplayMemberBinding = new Avalonia.Data.Binding(nameof(RecentDocument.Label)) };
        var message = new TextBlock { Text = "Open reuses tabs. Remove/Clear forgets history only.", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var open = new Button { Content = "Open" };
        open.Click += (_, _) => { if (list.SelectedItem is RecentDocument selected) Close(selected); };
        var remove = new Button { Content = "Remove selected" };
        remove.Click += async (_, _) =>
        {
            if (list.SelectedItem is not RecentDocument selected) return;
            try { await state.RemoveRecentDocumentAsync(selected.Path); list.ItemsSource = await state.GetRecentDocumentsAsync(); }
            catch (Exception exception) { message.Text = exception.Message; }
        };
        var clear = new Button { Content = "Clear history" };
        clear.Click += async (_, _) =>
        {
            try { await state.RemoveRecentDocumentAsync(null); list.ItemsSource = await state.GetRecentDocumentsAsync(); }
            catch (Exception exception) { message.Text = exception.Message; }
        };
        var close = new Button { Content = "Cancel" };
        close.Click += (_, _) => Close();
        var helpButton = new Button { Content = "Help" };
        helpButton.Click += (_, _) => help();
        var panel = new DockPanel { Margin = new Thickness(16), Children = { message, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { open, remove, clear, close, helpButton } }, list } };
        DockPanel.SetDock(message, Dock.Top);
        DockPanel.SetDock(panel.Children[1], Dock.Bottom);
        Content = panel;
    }
}
