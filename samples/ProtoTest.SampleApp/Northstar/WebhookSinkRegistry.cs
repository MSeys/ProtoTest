namespace ProtoTest.SampleApp.Northstar;

using ProtoTest.SampleApp.Contracts;

/// <summary>A configurable receiving endpoint used by the demo to observe webhook deliveries.</summary>
internal sealed class WebhookSinkRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Sink> _sinks = new(StringComparer.Ordinal);

    public (string Id, Uri Url) Create(Uri baseUrl, int failuresBeforeSuccess)
    {
        lock (_gate)
        {
            var id = $"sink_{Guid.NewGuid():N}";
            _sinks[id] = new Sink(failuresBeforeSuccess);
            return (id, new Uri(baseUrl, $"/test-support/webhook-sinks/{id}"));
        }
    }

    public IReadOnlyList<WebhookReceiptResponse> Receipts(string sinkId)
    {
        lock (_gate)
        {
            return Require(sinkId).Receipts.ToArray();
        }
    }

    public (bool Success, string? Error) Receive(string sinkId, string eventType, string signature, string body)
    {
        lock (_gate)
        {
            if (!_sinks.TryGetValue(sinkId, out var sink))
            {
                return (false, "sink_not_found");
            }

            if (sink.FailuresRemaining > 0)
            {
                sink.FailuresRemaining--;
                return (false, "sink_temporarily_unavailable");
            }

            sink.Receipts.Add(new WebhookReceiptResponse(
                $"rcp_{Guid.NewGuid():N}",
                sinkId,
                eventType,
                signature,
                body,
                DateTimeOffset.UtcNow));
            return (true, null);
        }
    }

    public static string? SinkIdFrom(Uri url) => System.IO.Path.GetFileName(url.AbsolutePath) is { Length: > 0 } id ? id : null;

    private Sink Require(string sinkId)
        => _sinks.TryGetValue(sinkId, out var sink) ? sink : throw NorthstarException.NotFound("webhook sink");

    private sealed class Sink(int failuresBeforeSuccess)
    {
        public int FailuresRemaining { get; set; } = failuresBeforeSuccess;
        public List<WebhookReceiptResponse> Receipts { get; } = [];
    }
}

/// <summary>Routes deliveries to <see cref="WebhookSinkRegistry"/> without opening a socket.</summary>
internal sealed class InProcessWebhookTransport(WebhookSinkRegistry sinks) : IWebhookTransport
{
    public Task<(bool Success, string? Error)> SendAsync(WebhookDispatch dispatch, CancellationToken cancellationToken)
    {
        var sinkId = WebhookSinkRegistry.SinkIdFrom(new Uri(dispatch.Url));
        var result = sinkId is null
            ? (false, (string?)"invalid_sink_url")
            : sinks.Receive(sinkId, dispatch.EventType, dispatch.Signature, dispatch.Payload);
        return Task.FromResult(result);
    }
}
