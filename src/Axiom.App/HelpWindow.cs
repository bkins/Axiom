using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform;
using Axiom.Core;

namespace Axiom.App;

internal sealed class HelpWindow : Window
{
    private readonly NativeWebView _view = new() { Name = "OfflineHelpPreview" };
    private readonly ComboBox _topics;
    private bool _ready;

    public HelpWindow(string? context, string cacheRoot, AppearancePreference appearance)
    {
        Title = "Axiom Help — offline User Guide";
        Width = 1000;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var guide = new MarkdownStructureService().Parse(OfflineHelp.Read());
        _topics = new ComboBox { Name = "HelpTopics", ItemsSource = guide.Headings, DisplayMemberBinding = new Avalonia.Data.Binding(nameof(DocumentHeading.Text)), MinWidth = 360 };
        _topics.SelectedItem = OfflineHelp.FindTopic(guide, context);
        _topics.SelectionChanged += async (_, _) => await NavigateTopicAsync();
        _view.EnvironmentRequested += (_, args) =>
        {
            if (args is WindowsWebView2EnvironmentRequestedEventArgs windows) windows.UserDataFolder = Path.Combine(cacheRoot, "OfflineHelpCache");
        };
        _view.AdapterCreated += (_, _) => _view.NavigateToString(new MarkdownPreviewService().RenderDocument(guide, null, appearance));
        _view.NavigationCompleted += async (_, args) => { _ready = args.IsSuccess; await NavigateTopicAsync(); };
        _view.NavigationStarted += (_, args) =>
        {
            if (args.Request is { } uri && uri.Scheme is not "about" and not "data") args.Cancel = true;
        };
        _view.NewWindowRequested += (_, args) => args.Handled = true;
        var close = new Button { Content = "Close" };
        close.Click += (_, _) => Close();
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(10), Children = { new TextBlock { Text = "Topic", VerticalAlignment = VerticalAlignment.Center }, _topics, close } };
        Content = new DockPanel { Children = { bar, _view } };
        DockPanel.SetDock(bar, Dock.Top);
    }

    private async Task NavigateTopicAsync()
    {
        if (!_ready || _topics.SelectedItem is not DocumentHeading heading) return;
        try { await _view.InvokeScript($"document.getElementById({JsonSerializer.Serialize(heading.Fragment)})?.scrollIntoView()"); }
        catch (Exception exception) { Title = "Axiom Help — " + exception.Message; }
    }

    internal NativeWebView PreviewForTesting => _view;
}
