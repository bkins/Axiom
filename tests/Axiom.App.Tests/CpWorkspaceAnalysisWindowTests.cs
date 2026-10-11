using System.Net;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Axiom.Core;
using Axiom.Infrastructure;
using Xunit;

namespace Axiom.App.Tests;

public sealed class CpWorkspaceAnalysisWindowTests
{
    [AvaloniaFact]
    public void Stale_Source_Refusal_Remains_Outside_Scrolling_Results_And_Never_Submits()
    {
        using var handler = new ResponseHandler();
        using var client = new CpDocumentAnalysisClient(new Uri("http://localhost:5273"), "test-only-credential-at-least-32-characters", handler);
        var source = AnalysisSnapshot.Capture("Wind.md", "# Wind\nMoving air\nBatteries");
        var window = new CpWorkspaceAnalysisWindow(client, [source], _ => false);
        window.Show();
        window.GetLogicalDescendants().OfType<CheckBox>().Single(control => control.Tag is AnalysisSnapshot).IsChecked = true;

        Click(window, "CpSemanticBuild");

        var status = Control<TextBlock>(window, "CpWorkspaceStatus");
        Assert.Contains("Sources changed", status.Text);
        Assert.Equal(0, handler.Requests);
        Assert.DoesNotContain(status, Control<ScrollViewer>(window, "CpSemanticScrollBody").GetLogicalDescendants());
        Assert.Contains(status, Control<StackPanel>(window, "CpSemanticActionBar").GetLogicalDescendants());
        Assert.Equal(Dock.Bottom, DockPanel.GetDock(Control<StackPanel>(window, "CpSemanticActionBar")));
        Assert.Equal(source.Text, Control<TextBox>(window, "CpWorkspaceSnapshotReview").Text!.Split('\n', 3)[2]);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Completed_Build_And_Unverified_Answer_Use_Persistent_Operation_Status()
    {
        using var handler = new ResponseHandler();
        using var client = new CpDocumentAnalysisClient(new Uri("http://localhost:5273"), "test-only-credential-at-least-32-characters", handler);
        var window = new CpWorkspaceAnalysisWindow(client, [AnalysisSnapshot.Capture("Wind.md", "# Wind\nMoving air")], _ => true);
        window.Show();
        window.GetLogicalDescendants().OfType<CheckBox>().Single(control => control.Tag is AnalysisSnapshot).IsChecked = true;

        Click(window, "CpSemanticBuild");
        await WaitAsync(() => Control<TextBlock>(window, "CpWorkspaceStatus").Text!.StartsWith("Index built:"));

        Assert.True(Control<Button>(window, "CpSemanticSearch").IsEnabled);
        var analyze = window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "Analyze reviewed documents"));
        analyze.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitAsync(() => Control<TextBlock>(window, "CpWorkspaceStatus").Text!.Contains("No validated citations"));
        Assert.Contains("cited-source navigation is unavailable", Control<TextBlock>(window, "CpWorkspaceStatus").Text);
        Assert.Equal(2, handler.Requests);
        window.Close();
    }

    private static T Control<T>(Window window, string name) where T : Control
        => window.GetLogicalDescendants().OfType<T>().Single(control => control.Name == name);
    private static void Click(Window window, string name) => Control<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task WaitAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (condition()) return;
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Assert.Fail("Operation did not finish.");
    }

    private sealed class ResponseHandler : HttpMessageHandler
    {
        internal int Requests { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var documents = body.RootElement.GetProperty("documents").EnumerateArray().ToArray();
            var response = new
            {
                protocolVersion = 1
              , clientRequestId = body.RootElement.GetProperty("clientRequestId").GetString()
              , references = documents.Select(source => new { referenceKey = source.GetProperty("referenceKey").GetString(), sourceHash = source.GetProperty("sourceHash").GetString() })
              , vectors = documents.Select(_ => new float[] { 1, 0 }).ToArray()
              , text = "Uncited model answer"
              , provider = "Ollama"
              , model = "fixture"
              , provenance = "provider_response_model"
              , simulated = false
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(response), Encoding.UTF8, "application/json") };
        }
    }
}
