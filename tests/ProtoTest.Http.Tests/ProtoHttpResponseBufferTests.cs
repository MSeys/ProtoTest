namespace ProtoTest.Http.Tests;

using System.Net;
using System.Text;

[TestFixture]
public sealed class ProtoHttpResponseBufferTests
{
    [Test]
    public async Task BufferAsync_ShouldReturnBytesAndKeepContentReadable()
    {
        using var response = Response(new StringContent("hello", Encoding.UTF8, "text/plain"));

        var bytes = await ProtoHttpResponseBuffer.BufferAsync(response, 10);

        Assert.Multiple(() =>
        {
            Assert.That(Encoding.UTF8.GetString(bytes), Is.EqualTo("hello"));
            Assert.That(response.Content.ReadAsStringAsync().Result, Is.EqualTo("hello"));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/plain"));
        });
    }

    [Test]
    public void BufferAsync_ShouldRejectNegativeLimit()
    {
        using var response = Response(new StringContent("hello"));
        Assert.ThrowsAsync<InvalidOperationException>(() => ProtoHttpResponseBuffer.BufferAsync(response, -1));
    }

    [Test]
    public void BufferAsync_ShouldRejectKnownOversizedContentBeforeReading()
    {
        using var response = Response(new ByteArrayContent(new byte[12]));
        var exception = Assert.ThrowsAsync<ProtoResponseTooLargeException>(() =>
            ProtoHttpResponseBuffer.BufferAsync(response, 10));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.MaximumBytes, Is.EqualTo(10));
            Assert.That(exception.ObservedBytes, Is.EqualTo(12));
        });
    }

    [Test]
    public void BufferAsync_ShouldRejectStreamThatGrowsPastLimit()
    {
        using var response = Response(new StreamContent(new MemoryStream(new byte[12])));
        response.Content.Headers.ContentLength = null;

        var exception = Assert.ThrowsAsync<ProtoResponseTooLargeException>(() =>
            ProtoHttpResponseBuffer.BufferAsync(response, 10));

        Assert.That(exception!.ObservedBytes, Is.EqualTo(12));
    }

    [Test]
    public async Task BufferAsync_ShouldAcceptExactlyTheConfiguredLimit()
    {
        using var response = Response(new ByteArrayContent(new byte[10]));
        Assert.That(await ProtoHttpResponseBuffer.BufferAsync(response, 10), Has.Length.EqualTo(10));
    }

    [Test]
    public async Task BufferAsync_ShouldNotRejectAHeadResponseWithALargeContentLength()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Head, "https://example.test/orders"),
            Content = new EmptyContentWithLength(1_000_000)
        };

        Assert.That(await ProtoHttpResponseBuffer.BufferAsync(response, 10), Is.Empty);
    }

    [Test]
    public async Task BufferAsync_ShouldNotRejectANoContentResponseWithALargeContentLength()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NoContent)
        {
            Content = new EmptyContentWithLength(1_000_000)
        };

        Assert.That(await ProtoHttpResponseBuffer.BufferAsync(response, 10), Is.Empty);
    }

    [Test]
    public void BufferAsync_ShouldHonorCancellation()
    {
        using var response = Response(new StreamContent(new CancellingStream()));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.That(async () => await ProtoHttpResponseBuffer.BufferAsync(response, 10, cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    private static HttpResponseMessage Response(HttpContent content) => new(HttpStatusCode.OK) { Content = content };

    private sealed class CancellingStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromCanceled<int>(cancellationToken);
    }

    private sealed class EmptyContentWithLength(long length) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => Task.CompletedTask;

        protected override bool TryComputeLength(out long computedLength)
        {
            computedLength = length;
            return true;
        }
    }
}
