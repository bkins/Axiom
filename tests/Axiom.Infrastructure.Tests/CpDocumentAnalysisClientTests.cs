using System.Net;
using System.Text;
using System.Text.Json;
using Axiom.Core;

namespace Axiom.Infrastructure.Tests;

public sealed class CpDocumentAnalysisClientTests
{
    private const string Credential = "test-only-dedicated-credential-at-least-32";

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://user:password@example.com")]
    [InlineData("https://example.com?key=secret")]
    [InlineData("https://example.com#secret")]
    public void Connection_Rejects_Unsafe_Transport_Or_Credential_Destinations(string endpoint)
        => Assert.Throws<ArgumentException>(() => new CpDocumentAnalysisClient(new Uri(endpoint), Credential));

    [Fact]
    public async Task Insights_Send_Only_Reviewed_Snapshot_And_Validate_Response_Hash()
    {
        var snapshot = AnalysisSnapshot.Capture("current-document", "unsaved source");
        using var handler = new AnalysisResponseHandler(false);
        using var client = new CpDocumentAnalysisClient(new Uri("http://localhost:5273"), Credential, handler);

        var response = await client.GetInsightsAsync(new[] { snapshot }, "Summarize");

        Assert.Equal("derived answer", response.Text);
        Assert.Equal("provider-model", response.Model);
        Assert.True(snapshot.Matches("unsaved source"));
        Assert.False(snapshot.Matches("changed source"));
        Assert.Contains("unsaved source", handler.RequestBody);
        Assert.DoesNotContain(Credential, handler.RequestBody);
        Assert.Equal(Credential, handler.Credential);
    }

    [Fact]
    public async Task Insights_Reject_Mismatched_Provider_Source_Hash()
    {
        using var handler = new AnalysisResponseHandler(true);
        using var client = new CpDocumentAnalysisClient(new Uri("http://localhost:5273"), Credential, handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetInsightsAsync(new[] { AnalysisSnapshot.Capture("doc", "reviewed source") }, null));
    }

    [Fact]
    public async Task Provider_Failure_Does_Not_Expose_Body_Or_Credentials()
    {
        using var handler = new AnalysisResponseHandler(false, HttpStatusCode.BadGateway);
        using var client = new CpDocumentAnalysisClient(new Uri("https://example.com"), Credential, handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetInsightsAsync(new[] { AnalysisSnapshot.Capture("doc", "private source") }, null));

        Assert.DoesNotContain("private", exception.Message);
        Assert.DoesNotContain(Credential, exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public async Task Cancellation_Prevents_Submission()
    {
        using var handler = new AnalysisResponseHandler(false);
        using var client = new CpDocumentAnalysisClient(new Uri("http://localhost:5273"), Credential, handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetInsightsAsync(new[] { AnalysisSnapshot.Capture("doc", "private source") }, null, cancellation.Token));

        Assert.Null(handler.RequestBody);
    }

    private sealed class AnalysisResponseHandler(bool mismatchedHash, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        internal string? RequestBody { get; private set; }
        internal string? Credential { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            Credential = request.Headers.GetValues("X-Document-Analysis-Key").Single();
            using var body = JsonDocument.Parse(RequestBody);
            var source = body.RootElement.GetProperty("documents")[0];
            var response = new
            {
                protocolVersion = 1
              , clientRequestId = body.RootElement.GetProperty("clientRequestId").GetString()
              , references = new[] { new { referenceKey = source.GetProperty("referenceKey").GetString(), sourceHash = mismatchedHash ? "wrong" : source.GetProperty("sourceHash").GetString() } }
              , text = "derived answer"
              , provider = "Ollama"
              , model = "provider-model"
              , provenance = "provider_response_model"
              , simulated = false
            };
            return new HttpResponseMessage(status) { Content = new StringContent(JsonSerializer.Serialize(response), Encoding.UTF8, "application/json") };
        }
    }
}
