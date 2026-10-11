using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

internal static class SemanticLayoutProbe
{
    internal static string? Root { get; private set; }
    internal static void Configure(string root)
    {
        Root = Path.GetFullPath(root);
        if (Directory.Exists(Root)) throw new IOException("Choose a fresh layout evidence directory.");
        Directory.CreateDirectory(Root);
        Environment.SetEnvironmentVariable("AXIOM_STATE_ROOT", Path.Combine(Root, "state"));
    }

    internal static async Task StartAsync(MainWindow owner)
    {
        CpWorkspaceAnalysisWindow? dialog = null;
        try
        {
            await owner.StartupCompleted;
            using var handler = new NoNetworkHandler();
            using var client = new CpDocumentAnalysisClient(new Uri("http://127.0.0.1:1"), "synthetic-layout-only-no-service-key", handler);
            var originalEditorText = owner.EditorTextForTesting;
            var source = AnalysisSnapshot.Capture("Wind.md", "# Wind\nMoving air\nBatteries");
            dialog = new CpWorkspaceAnalysisWindow(client, [source], _ => false);
            var pending = dialog.ShowDialog<SemanticMatch?>(owner);
            await Task.Delay(200);
            var initialStatus = Find<TextBlock>(dialog, "CpWorkspaceStatus").Text;
            var checks = new List<object>();
            foreach (var size in new[] { new Size(880, 800), new Size(600, 500) })
            {
                dialog.Width = size.Width; dialog.Height = size.Height;
                Find<TextBlock>(dialog, "CpWorkspaceStatus").Text = initialStatus;
                var body = Find<ScrollViewer>(dialog, "CpSemanticScrollBody");
                body.Offset = new Vector(0, 10000);
                await Task.Delay(250);
                var label = $"{size.Width}x{size.Height}";
                Capture(dialog, label + "-initial.png");
                Find<CheckBox>(dialog, "CpReuseVectors").IsChecked = false;
                dialog.GetLogicalDescendants().OfType<CheckBox>().Single(control => control.Tag is AnalysisSnapshot).IsChecked = true;
                Find<Button>(dialog, "CpSemanticBuild").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(100);
                Capture(dialog, label + "-refusal.png");
                var status = Find<TextBlock>(dialog, "CpWorkspaceStatus");
                var actions = Find<StackPanel>(dialog, "CpSemanticActionBar");
                var controls = actions.GetLogicalDescendants().OfType<Button>().Cast<Control>().Append(status).ToArray();
                var contained = controls.All(control =>
                {
                    var point = control.TranslatePoint(new Point(), dialog);
                    return point is { } origin && origin.X >= 0 && origin.Y >= 0
                        && origin.X + control.Bounds.Width <= dialog.ClientSize.Width + 1
                        && origin.Y + control.Bounds.Height <= dialog.ClientSize.Height + 1;
                });
                if (!contained || body.Viewport.Height < 100 || !status.Text!.StartsWith("Sources changed"))
                    throw new IOException("Semantic footer or content viewport is clipped at " + label);
                checks.Add(new { Size = label, FooterControlsContained = contained, ScrollingViewportHeight = body.Viewport.Height, StaleRefusal = true });
            }
            dialog.Close(null); await pending;
            await File.WriteAllTextAsync(Path.Combine(Root!, "result.json"), JsonSerializer.Serialize(new
            {
                Checks = checks, NativeRenderScaling = owner.RenderScaling,
                EditorBufferPreserved = owner.EditorTextForTesting == originalEditorText,
                RequestsSent = handler.Requests, HumanUsabilityAccepted = false
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error) { await File.WriteAllTextAsync(Path.Combine(Root!, "error.txt"), error.ToString()); }
        finally { dialog?.Close(null); owner.Close(); }
    }

    private static T Find<T>(Window window, string name) where T : Control => window.GetLogicalDescendants().OfType<T>().Single(control => control.Name == name);
    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        internal int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            throw new IOException("Layout probe must not submit a document.");
        }
    }
    private static void Capture(Window window, string name)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height), new Vector(96, 96));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(Root!, name), PngBitmapEncoderOptions.Default);
    }
}
