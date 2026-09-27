using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Markdig;
using Microsoft.Win32;

namespace WpfMarkdownEditor;

public partial class MainWindow : Window
{
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private string? _path;
    private bool _loading;
    private bool _previewReady;

    public MainWindow()
    {
        InitializeComponent();
        Editor.TextChanged += (_, _) => EditorTextChanged();
        _previewTimer.Tick += (_, _) => RenderPreview();
        Loaded += Window_OnLoaded;
    }

    private async void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await Preview.EnsureCoreWebView2Async();
            _previewReady = true;
            OpenInitialDocument();
        }
        catch (Exception exception)
        {
            StatusText.Text = $"WebView2 could not start: {exception.Message}";
            OpenInitialDocument();
        }
    }

    private void OpenInitialDocument()
    {
        var argument = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(File.Exists);
        var sample = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "sample.md"));
        var initialPath = argument ?? (File.Exists(sample) ? sample : null);

        if (initialPath is not null)
        {
            LoadDocument(initialPath);
        }
        else
        {
            StatusText.Text = "Open an existing Markdown file.";
            RenderPreview();
        }
    }

    private void Open_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Markdown files (*.md;*.markdown)|*.md;*.markdown|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            LoadDocument(dialog.FileName);
        }
    }

    private void Save_OnClick(object sender, RoutedEventArgs e) => SaveDocument();

    private void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SaveDocument();
            e.Handled = true;
        }
    }

    private void LoadDocument(string path)
    {
        try
        {
            _loading = true;
            _path = Path.GetFullPath(path);
            Editor.Text = File.ReadAllText(_path);
            Title = $"{Path.GetFileName(_path)} — Axiom WPF Spike";
            StatusText.Text = _path;
            RenderPreview();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not open file", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _loading = false;
        }
    }

    private void SaveDocument()
    {
        if (_path is null)
        {
            StatusText.Text = "Open an existing Markdown file before saving.";
            return;
        }

        try
        {
            File.WriteAllText(_path, Editor.Text);
            StatusText.Text = $"Saved {Path.GetFileName(_path)} at {DateTime.Now:T}";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not save file", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void EditorTextChanged()
    {
        if (_loading)
        {
            return;
        }

        StatusText.Text = _path is null ? "Unsaved text" : $"Unsaved changes — {_path}";
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void RenderPreview()
    {
        _previewTimer.Stop();
        if (!_previewReady)
        {
            return;
        }

        Preview.NavigateToString(PreviewHtml.FromMarkdown(Editor.Text, MarkdownPipeline));
    }
}

internal static class PreviewHtml
{
    public static string FromMarkdown(string markdown, MarkdownPipeline pipeline)
    {
        var body = Markdown.ToHtml(markdown, pipeline);
        return $$"""
            <!doctype html>
            <html><head><meta charset="utf-8"><style>
            body { font-family: Segoe UI, sans-serif; line-height: 1.55; max-width: 900px; margin: 0 auto; padding: 28px; color: #202124; }
            h1, h2, h3 { line-height: 1.2; } pre { padding: 14px; overflow: auto; background: #f3f4f6; }
            code { font-family: Cascadia Mono, Consolas, monospace; } blockquote { border-left: 4px solid #aaa; margin-left: 0; padding-left: 16px; color: #555; }
            table { border-collapse: collapse; } th, td { border: 1px solid #bbb; padding: 6px 10px; }
            </style></head><body>{{body}}</body></html>
            """;
    }
}
