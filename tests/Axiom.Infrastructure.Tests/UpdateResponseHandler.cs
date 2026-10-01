using System.Net;

namespace Axiom.Infrastructure.Tests;

internal sealed class UpdateResponseHandler(byte[] bytes) : HttpMessageHandler
{
    internal int Calls { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes), RequestMessage = request });
    }
}
