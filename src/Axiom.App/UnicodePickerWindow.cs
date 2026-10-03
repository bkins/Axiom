using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace Axiom.App;

internal sealed class UnicodePickerWindow : Window
{
    internal TextBox Search { get; }
    internal ListBox Choices { get; }
    private static readonly string[] Symbols =
    [
        "😀 Smile happy", "🙂 Slight smile", "😊 Smile eyes", "😂 Laugh tears", "😢 Sad crying", "😕 Confused", "🤔 Thinking", "👍 Thumbs up", "👎 Thumbs down",
        "👋 Wave hello", "🙏 Thanks please", "💡 Idea light bulb", "⚠️ Warning", "✅ Check done", "❌ Cross error", "⭐ Star favorite", "❤️ Heart love", "🎉 Celebration",
        "🚀 Rocket launch", "🐛 Bug", "🔧 Wrench fix", "📝 Note memo", "📁 Folder", "📌 Pin", "🔗 Link", "🔒 Lock", "🔍 Search", "💬 Comment", "📅 Calendar", "⏰ Clock alarm",
        "← Left arrow", "→ Right arrow", "↑ Up arrow", "↓ Down arrow", "↔ Bidirectional arrow", "⇒ Implies", "✓ Check", "✗ Cross", "• Bullet", "… Ellipsis", "— Em dash",
        "© Copyright", "® Registered", "™ Trademark", "° Degree", "± Plus minus", "× Multiply", "÷ Divide", "≤ Less equal", "≥ Greater equal", "≠ Not equal", "∞ Infinity",
        "α Alpha", "β Beta", "γ Gamma", "δ Delta", "π Pi", "Σ Sigma sum", "µ Micro", "Ω Omega ohm", "½ Half", "¼ Quarter", "¾ Three quarters"
    ];

    internal UnicodePickerWindow()
    {
        Title = "Insert symbol or emoji"; Width = 440; Height = 460; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var search = new TextBox { Name = "UnicodeSearch", PlaceholderText = "Search by name or symbol" };
        var list = new ListBox { Name = "UnicodeChoices", ItemsSource = Symbols };
        Search = search; Choices = list;
        var insert = new Button { Content = "Insert" };
        var cancel = new Button { Content = "Cancel" };
        void Choose() { if (list.SelectedItem is string item) Close(item[..item.IndexOf(' ')]); }
        search.TextChanged += (_, _) => { list.ItemsSource = Symbols.Where(item => item.Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToArray(); list.SelectedIndex = 0; };
        insert.Click += (_, _) => Choose(); cancel.Click += (_, _) => Close();
        list.DoubleTapped += (_, _) => Choose();
        KeyDown += (_, args) => { if (args.Key == Key.Enter) { Choose(); args.Handled = true; } else if (args.Key == Key.Escape) { Close(); args.Handled = true; } };
        Opened += (_, _) => { search.Focus(); list.SelectedIndex = 0; };
        var grid = new Grid { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        grid.Children.Add(search); Grid.SetRow(list, 1); grid.Children.Add(list);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { insert, cancel } };
        Grid.SetRow(buttons, 2); grid.Children.Add(buttons); Content = grid;
    }
}
