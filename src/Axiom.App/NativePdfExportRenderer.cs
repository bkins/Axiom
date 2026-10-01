using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

internal sealed class NativePdfExportRenderer(string cacheRoot) : IPdfExportRenderer
{
    public async Task<PdfRenderResult> RenderAsync(string html, PdfPageSize pageSize, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            return await Dispatcher.UIThread.InvokeAsync(() => RenderOnUiThreadAsync(html, pageSize, timeout.Token));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException("PDF rendering timed out. Reduce the document or images and retry; existing output was preserved.");
        }
    }

    private async Task<PdfRenderResult> RenderOnUiThreadAsync(string html, PdfPageSize pageSize, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var view = new NativeWebView { Background = Avalonia.Media.Brushes.White };
        var host = new Window
        {
            Content = view
          , Width = 816
          , Height = 1056
          , Position = new PixelPoint(-32000, -32000)
          , ShowActivated = false
          , ShowInTaskbar = false
          , WindowDecorations = WindowDecorations.None
          , Title = "Axiom PDF export renderer"
        };
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        view.EnvironmentRequested += (_, args) =>
        {
            if (args is WindowsWebView2EnvironmentRequestedEventArgs windows)
                windows.UserDataFolder = Path.Combine(cacheRoot, "PdfExportCache");
        };
        view.AdapterCreated += (_, _) => view.NavigateToString(html);
        view.NavigationCompleted += (_, args) =>
        {
            if (args.IsSuccess) loaded.TrySetResult();
            else loaded.TrySetException(new IOException("PDF export content could not load in the native renderer."));
        };
        view.NavigationStarted += (_, args) =>
        {
            if (args.Request is { } uri && uri.Scheme is not "about" and not "data") args.Cancel = true;
        };
        view.NewWindowRequested += (_, args) => args.Handled = true;
        try
        {
            host.Show();
            await loaded.Task.WaitAsync(cancellationToken);
            var ready = false;
            while (!ready)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ready = await view.InvokeScript("document.fonts.status === 'loaded' && Array.from(document.images).every(image => image.complete)") == "true";
                if (!ready) await Task.Delay(50, cancellationToken);
            }
            var warnings = new List<string>();
            var oversized = await view.InvokeScript("Array.from(document.querySelectorAll('tr')).filter(row => row.getBoundingClientRect().height > 800).length");
            if (int.TryParse(oversized, out var count) && count > 0)
                warnings.Add("Very tall table rows may split across PDF pages; inspect their continuation in the exported document.");
            if (!OperatingSystem.IsWindows() || view.TryGetPlatformHandle() is not IWindowsWebView2PlatformHandle handle)
                throw new NotSupportedException("PDF export requires WebView2 on Windows for verified page geometry.");
            var printing = WindowsPdfPrinter.PrintAsync(handle, pageSize);
            Stream stream;
            try { stream = await printing.WaitAsync(cancellationToken); }
            catch
            {
                _ = printing.ContinueWith(completed =>
                {
                    if (completed.Status == TaskStatus.RanToCompletion) completed.Result.Dispose();
                    else _ = completed.Exception;
                }, TaskScheduler.Default);
                throw;
            }
            await using (stream)
            {
                using var output = new MemoryStream();
                var buffer = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    if (output.Length + read > 128 * 1024 * 1024) throw new IOException("PDF exceeds 128 MiB. Reduce the document or images.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                return new PdfRenderResult(output.ToArray(), warnings);
            }
        }
        catch (NotSupportedException)
        {
            throw new IOException("This native browser cannot export PDF. Install or update the WebView2 Runtime and retry.");
        }
        finally { host.Close(); }
    }
}
