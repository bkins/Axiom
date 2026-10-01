using System.Net;

namespace Axiom.App;

internal sealed class PackageUpdateFixtureHandler(byte[] offer, string archive) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        HttpContent content = request.RequestUri!.AbsolutePath.EndsWith("offer.json", StringComparison.Ordinal)
            ? new ByteArrayContent(offer) : new StreamContent(File.OpenRead(archive));
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content, RequestMessage = request });
    }
}
