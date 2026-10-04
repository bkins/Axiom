using System.Text;
using Avalonia.Controls;

namespace Axiom.App;

/// <summary>Large offline HTML uses a private file rather than WebView2's bounded string API.</summary>
internal sealed class NativeHtmlContent(string cacheRoot) : IDisposable
{
    private readonly List<string> _ownedFiles = [];
    internal Uri? CurrentNavigation { get; private set; }

    internal void Load(NativeWebView view, string html)
        => Load(html, text => view.NavigateToString(text), view.Navigate);

    internal void Load(string html, Action<string> loadString, Action<Uri> loadFile)
    {
        // Conservative UTF-16/UTF-8 budget stays safely below the native 2 MiB limit.
        if (html.Length <= 512 * 1024 && Encoding.UTF8.GetByteCount(html) <= 1024 * 1024)
        {
            CurrentNavigation = null;
            loadString(html);
            return;
        }
        var root = Path.GetFullPath(cacheRoot);
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".html");
        File.WriteAllText(path, html, new UTF8Encoding(false));
        _ownedFiles.Add(path);
        CurrentNavigation = new Uri(path);
        loadFile(CurrentNavigation);
    }

    internal bool Allows(Uri uri) => CurrentNavigation is { } current && uri.IsFile &&
        uri.Query.Length == 0 && uri.Fragment.Length == 0 && uri.Equals(current);

    public void Dispose()
    {
        foreach (var path in _ownedFiles)
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        _ownedFiles.Clear();
        CurrentNavigation = null;
    }
}
