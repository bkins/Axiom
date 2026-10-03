using System.Collections.Concurrent;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Axiom.App;

public partial class App
{
    private static readonly ConcurrentQueue<string> PendingActivations = new();
    private static MainWindow? _activationWindow;

    internal static void RequestActivation(string? path)
    {
        PendingActivations.Enqueue(path ?? "");
        if (_activationWindow is not null) Dispatcher.UIThread.Post(DrainActivations);
    }

    private static async void DrainActivations()
    {
        if (_activationWindow is not { } window) return;
        await window.StartupCompleted;
        while (PendingActivations.TryDequeue(out var path))
        {
            if (!window.IsVisible) window.Show();
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
            if (path.Length > 0) await window.OpenForwardedDocumentAsync(path);
        }
    }
}
