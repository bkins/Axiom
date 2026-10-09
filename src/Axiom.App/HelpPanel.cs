using Avalonia.Automation;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform;
using Axiom.Core;

namespace Axiom.App;

internal sealed class HelpPanel : UserControl
{
    private readonly NativeWebView _view = new() { Name = "PersistentHelpPreview" };
    private readonly DocumentStructure _guide;
    private readonly ComboBox _topics;
    private readonly TextBox _search;
    private readonly string _markdown;
    private readonly NativeHtmlContent _content;
    private readonly TextBlock _contentStatus;
    private bool _ready;
    private bool _selectingMatch;
    private AppearancePreference _appearance;

    public HelpPanel(string cacheRoot, AppearancePreference appearance, Action close, Action<double> resize, Action? walkthrough = null, IReadOnlyList<AxiomCommand>? commands = null)
    {
        _appearance = appearance;
        _markdown = OfflineHelp.Read(commands);
        _guide = new MarkdownStructureService().Parse(_markdown);
        _topics = new ComboBox { Name = "PersistentHelpTopics", ItemsSource = _guide.Headings, DisplayMemberBinding = new Avalonia.Data.Binding(nameof(DocumentHeading.Text)), HorizontalAlignment = HorizontalAlignment.Stretch };
        _topics.SelectionChanged += async (_, _) => { if (!_selectingMatch) await NavigateAsync(); };
        var search = new TextBox { Name = "HelpSearch", PlaceholderText = "Search Help text", MaxLength = 200 };
        _search = search;
        var results = new ListBox { Name = "HelpSearchResults", MaxHeight = 170, DisplayMemberBinding = new Avalonia.Data.Binding(nameof(HelpSearchResult.Label)) };
        AutomationProperties.SetName(_topics, "Browse offline Help topics");
        AutomationProperties.SetName(search, "Search offline Help headings and body");
        AutomationProperties.SetName(results, "Matching Help passages");
        AutomationProperties.SetName(_view, "Offline Help content");
        var status = _contentStatus = new TextBlock { Text = "Browse topics or search the offline guide.", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        search.TextChanged += (_, _) =>
        {
            var matches = new HelpSearchService().Search(_markdown, search.Text ?? string.Empty);
            results.ItemsSource = matches;
            status.Text = string.IsNullOrWhiteSpace(search.Text) ? "Enter text to search headings and guidance." : matches.Count == 0 ? "No matching Help text." : matches.Count + " matching passages.";
        };
        results.SelectionChanged += async (_, _) =>
        {
            if (results.SelectedItem is not HelpSearchResult selected) return;
            _selectingMatch = true;
            _topics.SelectedItem = _guide.Headings.FirstOrDefault(heading => heading.Fragment == selected.Fragment);
            _selectingMatch = false;
            await NavigateAsync(search.Text, selected.Excerpt);
        };
        var closeButton = new Button { Content = "Close Help" };
        closeButton.Click += (_, _) => close();
        var smaller = new Button { Content = "−", [AutomationProperties.NameProperty] = "Narrow Help panel" };
        var larger = new Button { Content = "+", [AutomationProperties.NameProperty] = "Widen Help panel" };
        smaller.Click += (_, _) => resize(-40);
        larger.Click += (_, _) => resize(40);
        var walkthroughButton = new Button { Content = "Guided walkthrough", [AutomationProperties.NameProperty] = "Open captioned app walkthrough", IsVisible = walkthrough is not null };
        walkthroughButton.Click += (_, _) => walkthrough?.Invoke();
        var header = new StackPanel
        {
            Margin = new Thickness(8),
            Spacing = 6,
            Children =
        {
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { closeButton, smaller, larger } },
            walkthroughButton, _topics, search, status, results
        }
        };
        var browserRegion = new Border { Child = _view, ClipToBounds = true };
        var panel = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Children = { header, browserRegion } };
        Grid.SetRow(browserRegion, 1);
        header.ZIndex = 1;
        Content = panel;
        _view.EnvironmentRequested += (_, args) => { if (args is WindowsWebView2EnvironmentRequestedEventArgs windows) windows.UserDataFolder = Path.Combine(cacheRoot, "PersistentHelpCache"); };
        _content = new(Path.Combine(cacheRoot, "HelpHtml"));
        DetachedFromVisualTree += (_, _) => _content.Dispose();
        void LoadContent()
        {
            try { _content.Load(_view, new MarkdownPreviewService().RenderDocument(_guide, null, _appearance)); }
            catch (Exception error) { status.Text = "Help content could not load: " + error.Message; }
        }
        _view.AdapterCreated += (_, _) => LoadContent();
        _view.NavigationCompleted += async (_, args) => { _ready = args.IsSuccess; await NavigateAsync(); };
        _view.NavigationStarted += (_, args) => { if (args.Request is { } uri && uri.Scheme is not "about" and not "data" && !_content.Allows(uri)) args.Cancel = true; };
        _view.NewWindowRequested += (_, args) => args.Handled = true;
    }

    public void SelectContext(string? context)
    {
        if (context is null && _topics.SelectedItem is not null) return;
        _topics.SelectedItem = OfflineHelp.FindTopic(_guide, context);
    }

    internal NativeWebView PreviewForTesting => _view;
    internal void FocusSearch() => _search.Focus();
    internal string? SelectedTopicForTesting => (_topics.SelectedItem as DocumentHeading)?.Text;

    internal void SetAppearance(AppearancePreference appearance)
    {
        if (_appearance == appearance) return;
        _appearance = appearance;
        if (_ready)
        {
            try { _content.Load(_view, new MarkdownPreviewService().RenderDocument(_guide, null, _appearance)); }
            catch (Exception error) { _contentStatus.Text = "Help content could not load: " + error.Message; }
        }
    }

    private async Task NavigateAsync(string? query = null, string? excerpt = null)
    {
        if (!_ready || _topics.SelectedItem is not DocumentHeading heading) return;
        try
        {
            var fragment = JsonSerializer.Serialize(heading.Fragment);
            var search = JsonSerializer.Serialize(query?.Trim() ?? string.Empty);
            var passage = JsonSerializer.Serialize(excerpt ?? string.Empty);
            await _view.InvokeScript($$"""
                (() => {
                  document.querySelectorAll('mark').forEach(mark => mark.replaceWith(document.createTextNode(mark.textContent)));
                  document.body.normalize();
                  const heading = document.getElementById({{fragment}});
                  if (!heading) return;
                  heading.scrollIntoView();
                  const query = {{search}}.toLocaleLowerCase();
                  if (!query) return;
                  const excerpt = {{passage}};
                  let node = heading, end = heading.nextElementSibling;
                  while (end && !/^H[1-6]$/.test(end.tagName)) end = end.nextElementSibling;
                  while (node && node !== end) {
                    const walker = document.createTreeWalker(node, NodeFilter.SHOW_TEXT), texts = [];
                    while(walker.nextNode()) texts.push(walker.currentNode);
                    for (const text of texts) {
                      const index = text.data.toLocaleLowerCase().indexOf(query);
                      if(index < 0) continue;
                      const tail = text.splitText(index), rest = tail.splitText(query.length);
                      const mark = document.createElement('mark'); mark.textContent = tail.data; tail.replaceWith(mark);
                      if (!excerpt || excerpt.includes(node.textContent.trim().slice(0,35)) || node.textContent.includes(excerpt)) { mark.scrollIntoView({block:'center'}); return; }
                    }
                    node = node.nextElementSibling;
                  }
                })()
                """);
        }
        catch (Exception) { /* Native view can detach while closing Help; the topic selection is retained. */ }
    }
}
