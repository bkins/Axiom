using System.Net;
using System.Reflection;
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

public sealed partial class MainWindowTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Citation_Button_Navigates_Exact_Unsaved_Range_And_Refuses_Stale_Sources(bool stale)
    {
        var root = Path.Combine(Path.GetTempPath(), "AxiomCitationUi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        MainWindow? window = null;
        try
        {
            var solarPath = Path.Combine(root, "Solar.md");
            var windPath = Path.Combine(root, "Wind.md");
            const string solarText = "# Solar\nSolar generation.\nBatteries store surplus.";
            await File.WriteAllTextAsync(solarPath, solarText, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(windPath, "# Wind\nMoving air generates electricity.", TestContext.Current.CancellationToken);
            window = CreateWindow(root);
            window.FindControl<Grid>("EditorPreviewLayout")!.Children.Remove(window.FindControl<NativeWebView>("Preview")!);
            window.Show();
            await window.StartupCompleted;
            await window.LoadFolderAsync(root);
            await window.OpenDocumentAsync(solarPath);
            window.FindControl<AvaloniaEdit.TextEditor>("Editor")!.Document.Insert(solarText.Length, "\nUnsaved draft");
            await window.OpenDocumentAsync(windPath);
            using var client = new CpDocumentAnalysisClient(new Uri("http://localhost:5273"), "test-only-credential-at-least-32-characters", new CitationUiHandler());
            typeof(MainWindow).GetField("_cpAnalysisClient", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, client);
            var command = window.ExecuteCommandAsync("workspace.cpAnalysis");
            await CitationWaitAsync(() => window.OwnedWindows.OfType<CpWorkspaceAnalysisWindow>().Any());
            var dialog = window.OwnedWindows.OfType<CpWorkspaceAnalysisWindow>().Single();
            foreach (var selector in dialog.GetLogicalDescendants().OfType<CheckBox>().Where(control => control.Tag is AnalysisSnapshot)) selector.IsChecked = true;
            dialog.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "Analyze reviewed documents")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var citations = dialog.GetLogicalDescendants().OfType<ListBox>().Single(control => control.Name == "CpResolvedCitations");
            await CitationWaitAsync(() => citations.ItemCount == 1);
            citations.SelectedIndex = 0;
            if (stale) window.SetEditorTextForTesting(window.EditorTextForTesting + "\nChanged after review");
            dialog.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "Open selected cited source range")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (stale)
            {
                await CitationWaitAsync(() => dialog.GetLogicalDescendants().OfType<TextBlock>().Any(control => control.Text == "Select a resolved citation with current source hashes first."));
                Assert.Equal(windPath, window.PathContextForTesting);
                Assert.True(dialog.IsVisible);
                dialog.Close(null);
            }
            await command;
            if (!stale)
            {
                Assert.Equal(solarPath, window.PathContextForTesting);
                Assert.Equal("Batteries store surplus.", window.EditorSelectedTextForTesting.Trim());
                Assert.Equal(solarText + "\nUnsaved draft", window.EditorTextForTesting);
                Assert.True(window.EditorCanUndoForTesting);
            }
            Assert.Equal(solarText, await File.ReadAllTextAsync(solarPath, TestContext.Current.CancellationToken));
        }
        finally
        {
            if (window is not null) { foreach (var child in window.OwnedWindows.ToArray()) child.Close(); window.Close(); }
            Directory.Delete(root, true);
        }
    }

    private static async Task CitationWaitAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 300; attempt++) { if (condition()) return; await Task.Delay(10, TestContext.Current.CancellationToken); }
        Assert.Fail("Citation UI did not finish.");
    }

    private sealed class CitationUiHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var documents = body.RootElement.GetProperty("documents").EnumerateArray().ToArray();
            var key = documents.Single(source => source.GetProperty("referenceKey").GetString()!.EndsWith("Solar.md", StringComparison.Ordinal)).GetProperty("referenceKey").GetString();
            var response = new
            {
                protocolVersion = 1, clientRequestId = body.RootElement.GetProperty("clientRequestId").GetString(),
                references = documents.Select(source => new { referenceKey = source.GetProperty("referenceKey").GetString(), sourceHash = source.GetProperty("sourceHash").GetString() }),
                text = $"Batteries store surplus. [[{key}|L3-L3]]", provider = "Ollama", model = "fixture", provenance = "provider_response_model", simulated = false
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(response), Encoding.UTF8, "application/json") };
        }
    }
}
