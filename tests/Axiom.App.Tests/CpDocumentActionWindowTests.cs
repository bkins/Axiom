using System.Net;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Axiom.App;
using Axiom.Core;
using Axiom.Infrastructure;
using Xunit;
namespace Axiom.App.Tests;
public sealed class CpDocumentActionWindowTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Suggestion_Requires_Preview_And_Explicit_Apply_And_Rejects_Stale_Source(bool stale)
    {
        var text = "# Unsaved source"; var applied = 0; var connections = 0;
        using var handler = new Handler();
        using var client = new CpDocumentAnalysisClient(new Uri("http://localhost:5289"), "test-only-key-at-least-thirty-two-characters", handler);
        var window = new CpDocumentActionWindow(() => AnalysisSnapshot.Capture("current", text), snapshot => snapshot.Matches(text),
            (_, _) => { connections++; return Task.FromResult(client); }, (_, proposal) => { text = proposal; applied++; });
        window.Show();
        Assert.Equal(0, connections); Assert.Equal(0, handler.Requests);
        Click(window, "CpActionSuggest");
        await Wait(() => Control<Button>(window, "CpActionPreview").IsEnabled);
        Assert.Equal("# Unsaved source", text); Assert.Equal(0, applied);
        Assert.False(Control<Button>(window, "CpActionApply").IsEnabled);
        Assert.Equal("# Unsaved source", handler.Source);
        Assert.Equal("/api/document-analysis/edits", handler.LastPath);
        Click(window, "CpActionPreview");
        await Wait(() => window.OwnedWindows.OfType<LineDiffWindow>().Any());
        window.OwnedWindows.OfType<LineDiffWindow>().Single().Close();
        await Wait(() => Control<Button>(window, "CpActionApply").IsEnabled);
        if (stale) text = "Newer edits";
        Click(window, "CpActionApply");
        Assert.Equal(stale ? "Newer edits" : "# Proposed revision", text);
        Assert.Equal(stale ? 0 : 1, applied);
        window.Close();
    }
    [AvaloniaFact]
    public async Task Analyze_On_Open_Captures_Current_Buffer_Without_A_Separate_Connection_Step()
    {
        using var handler = new Handler();
        using var client = new CpDocumentAnalysisClient(new Uri("http://localhost:5289"), "test-only-key-at-least-thirty-two-characters", handler);
        var window = new CpDocumentActionWindow(() => AnalysisSnapshot.Capture("draft", "Current unsaved text"), _ => true,
            (_, _) => Task.FromResult(client), (_, _) => Assert.Fail("Analysis must not apply text"), true);
        window.Show(); await Wait(() => Control<TextBox>(window, "CpActionOutput").Text?.Contains("DERIVED ANALYSIS") == true);
        Assert.Equal("Current unsaved text", handler.Source); Assert.Equal(1, handler.Requests); window.Close();
    }
    [AvaloniaTheory]
    [InlineData(HttpStatusCode.Unauthorized, "authenticate")]
    [InlineData(HttpStatusCode.GatewayTimeout, "took too long")]
    public async Task Failed_Actions_Show_Useful_Error_And_Enable_Retry_Without_Applying(HttpStatusCode code, string message)
    {
        using var handler = new Handler { ResponseStatus = code };
        using var client = new CpDocumentAnalysisClient(new Uri("http://localhost:5290"), "test-only-key-at-least-thirty-two-characters", handler);
        var window = new CpDocumentActionWindow(() => AnalysisSnapshot.Capture("draft", "Unchanged source"), _ => true,
            (_, _) => Task.FromResult(client), (_, _) => Assert.Fail("Failed request must not apply"));
        window.Show();

        Click(window, "CpActionSuggest");
        await Wait(() => Control<TextBlock>(window, "CpActionStatus").Text?.Contains(message) == true);

        Assert.True(Control<Button>(window, "CpActionSuggest").IsEnabled);
        Assert.False(Control<Button>(window, "CpActionApply").IsEnabled);
        Assert.False(Control<Button>(window, "CpActionPreview").IsEnabled);
        window.Close();
    }

    private static T Control<T>(Window window, string name) where T : Control => window.GetLogicalDescendants().OfType<T>().Single(control => control.Name == name);
    private static void Click(Window window, string name) => Control<Button>(window,name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task Wait(Func<bool> test) { for(var i=0;i<300;i++){if(test())return;await Task.Delay(10,TestContext.Current.CancellationToken);} Assert.Fail("CP action did not complete"); }
    private sealed class Handler : HttpMessageHandler
    {
        internal int Requests; internal string? Source; internal string? LastPath;
        internal HttpStatusCode ResponseStatus = HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (ResponseStatus != HttpStatusCode.OK) return new(ResponseStatus);
            Requests++; LastPath = request.RequestUri!.AbsolutePath; using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var docs=body.RootElement.GetProperty("documents").EnumerateArray().ToArray(); Source=docs[0].GetProperty("text").GetString();
            var result=new {protocolVersion=1,clientRequestId=body.RootElement.GetProperty("clientRequestId").GetString(),references=docs.Select(doc=>new{referenceKey=doc.GetProperty("referenceKey").GetString(),sourceHash=doc.GetProperty("sourceHash").GetString()}),text=JsonSerializer.Serialize(new { edits = new[] { new { oldText = Source, newText = "# Proposed revision" } } }),provider="Ollama",model="fixture",provenance="provider_response_model",simulated=false};
            return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(result),Encoding.UTF8,"application/json")};
        }
    }
}
