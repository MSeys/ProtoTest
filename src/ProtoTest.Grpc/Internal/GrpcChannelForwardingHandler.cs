namespace ProtoTest.Grpc.Internal;

using System.Net.Http;

/// <summary>
/// Sends channel traffic through an existing HTTP client - the in-process test server transport - so a
/// gRPC channel reaches an application hosted in the test process without a listening socket.
/// </summary>
internal sealed class GrpcChannelForwardingHandler(HttpClient client) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
        => client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
}
