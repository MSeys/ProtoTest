namespace ProtoTest.Messaging;

using System.Globalization;
using ProtoTest.Core;
using ProtoTest.Json;

/// <summary>
/// The test-side messaging API over the configured broker. Publishes and awaits are traced as
/// <c>messaging.publish</c> and <c>messaging.await</c> operations with the payload as a section, and
/// recorded as observations for the trace and for a collector a suite registers; the package itself
/// ships no collector and aggregates no destinations.
/// </summary>
public sealed class ProtoMessageClient
{
    private static readonly ProtoAttachmentFailure AttachmentFailure =
        new("messaging.attachment.failed", ProtoMessagingProtocol.Protocol.TraceSource, "Messaging attachment");

    private readonly ProtoExecutionContext _context;
    private readonly IProtoMessageBroker _broker;
    private readonly IProtoMessageConsumer _consumer;
    private readonly MessagingOptions _options;
    private readonly IReadOnlyDictionary<string, Exception> _prepareFailures;
    private int _captureSequence;

    internal ProtoMessageClient(
        ProtoExecutionContext context,
        IProtoMessageBroker broker,
        IProtoMessageConsumer consumer,
        MessagingOptions options,
        IReadOnlyDictionary<string, Exception> prepareFailures)
    {
        _context = context;
        _broker = broker;
        _consumer = consumer;
        _options = options;
        _prepareFailures = prepareFailures;
    }

    /// <summary>
    /// Publishes one message to the exchange named by <paramref name="destination"/>, optionally with
    /// headers and a content type. A queue destination (<see cref="ProtoDestination.Queue"/>) is
    /// consumed, not published to, and fails naming the queue.
    /// </summary>
    public Task PublishAsync(
        string destination,
        string? payload = null,
        IReadOnlyDictionary<string, string?>? headers = null,
        string? contentType = null,
        CancellationToken cancellationToken = default)
        => PublishCoreAsync(destination, routingKey: null, payload, headers, contentType, cancellationToken);

    /// <summary>
    /// Publishes one message to <paramref name="destination"/> under <paramref name="routingKey"/>, which
    /// the RabbitMQ adapter sends as the AMQP routing key and records on consumed messages. The payload
    /// stays a required argument so this overload does not compete with the payload-only one. A transport
    /// that addresses by something other than a routing key (MassTransit's contract types) fails the
    /// publish naming the destination instead of dropping the key.
    /// </summary>
    public Task PublishAsync(
        string destination,
        string routingKey,
        string? payload,
        IReadOnlyDictionary<string, string?>? headers = null,
        string? contentType = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routingKey);
        return PublishCoreAsync(destination, routingKey, payload, headers, contentType, cancellationToken);
    }

    private async Task PublishCoreAsync(
        string destination,
        string? routingKey,
        string? payload,
        IReadOnlyDictionary<string, string?>? headers,
        string? contentType,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        if (ProtoDestination.IsQueue(destination))
        {
            throw new InvalidOperationException(
                $"Cannot publish to '{destination}': the destination addresses a queue, and messaging " +
                "publishes to exchanges. Publish to the exchange that feeds the queue, or consume the " +
                "queue with AwaitAsync.");
        }

        var scope = _context.Trace
            .Operation(ProtoMessagingProtocol.Publish, $"Messaging · publish {destination}", ProtoMessagingProtocol.Protocol.TraceSource)
            .With("messaging.system", _broker.Name)
            .With("messaging.destination", destination);
        if (routingKey is not null)
        {
            scope = scope.With("messaging.routing_key", routingKey);
        }

        using var operation = scope.Begin();
        var attachmentOptions = _context.TryService<MessagingAttachmentOptions>();
        if (payload is { Length: > 0 })
        {
            operation.AddSection(new ProtoTraceSection(
                "Message",
                ProtoTraceSectionKind.Code,
                Content: ProtoTraceContent.Preview(JsonDiagnosticSanitizer.Sanitize(payload, attachmentOptions)),
                Language: contentType));
        }

        try
        {
            await _broker.PublishAsync(
                new ProtoMessage(destination, payload, headers, contentType) { RoutingKey = routingKey },
                cancellationToken);
            if (payload is { Length: > 0 } && attachmentOptions?.CapturePublishedPayloads == true)
            {
                Capture(
                    operation,
                    attachmentOptions,
                    $"message-publish-{destination}-{Interlocked.Increment(ref _captureSequence)}-payload",
                    payload,
                    contentType,
                    $"Published payload · {destination}");
            }

            operation.Succeed();
            _context.RecordObservation(new ProtoObservation(
                _broker.Name,
                ProtoMessagingProtocol.PublishObservationKind,
                destination,
                Metadata: new Dictionary<string, object> { ["messaging.system"] = _broker.Name }));
        }
        catch (Exception exception)
        {
            operation.Fail(exception);
            RecordFailure(destination, routingKey, exception, cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Waits for the first message on <paramref name="destination"/> matching <paramref name="predicate"/>
    /// within the timeout (the configured default when none is given). Failing to arrive is a test
    /// failure, not a sleep. An exchange destination waits on a tap prepared for it; a queue destination
    /// (<c>queue:{name}</c>, see <see cref="ProtoDestination.Queue"/>) waits on the queue itself, and an
    /// adapter whose broker has no queues fails naming the transport. A destination whose tap could not
    /// be declared during setup fails the await with the adapter's named error instead of timing out.
    /// </summary>
    public Task<ProtoMessage> AwaitAsync(
        string destination,
        Func<ProtoMessage, bool> predicate,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        => AwaitCoreAsync(destination, routingKey: null, predicate, timeout, cancellationToken);

    /// <summary>
    /// Waits for the first message on <paramref name="destination"/> matching <paramref name="predicate"/>
    /// that the transport carried under <paramref name="routingKey"/>. On RabbitMQ the tap binds the
    /// routing key as well as the exchange, so a direct exchange delivers it; a timeout names both the
    /// destination and the routing key.
    /// </summary>
    public Task<ProtoMessage> AwaitAsync(
        string destination,
        string routingKey,
        Func<ProtoMessage, bool> predicate,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routingKey);
        return AwaitCoreAsync(destination, routingKey, predicate, timeout, cancellationToken);
    }

    private async Task<ProtoMessage> AwaitCoreAsync(
        string destination,
        string? routingKey,
        Func<ProtoMessage, bool> predicate,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(predicate);
        var effective = timeout ?? _options.DefaultTimeout;
        var scope = _context.Trace
            .Operation(ProtoMessagingProtocol.Await, $"Messaging · await {destination}", ProtoMessagingProtocol.Protocol.TraceSource)
            .With("messaging.system", _broker.Name)
            .With("messaging.destination", destination)
            .With("messaging.timeout_ms", effective.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture));
        if (routingKey is not null)
        {
            scope = scope.With("messaging.routing_key", routingKey);
        }

        using var operation = scope.Begin();
        var attachmentOptions = _context.TryService<MessagingAttachmentOptions>();
        try
        {
            // A destination the setup hook could not prepare (a missing exchange) fails its own tests
            // with the adapter's error; tests that never await it stay unaffected.
            if (_prepareFailures.TryGetValue(destination, out var prepareFailure))
            {
                throw prepareFailure;
            }

            ProtoMessage message;
            try
            {
                message = await _consumer.AwaitAsync(destination, routingKey, predicate, effective, cancellationToken);
            }
            catch (TimeoutException exception) when (routingKey is not null)
            {
                // The queue's timeout names the destination; the routing-key form names what the test
                // asked for as well, so a wrong key is not read as a missing destination.
                throw new TimeoutException(
                    $"No message matching the predicate arrived on '{destination}' with routing key " +
                    $"'{routingKey}' within {effective.TotalSeconds:0.###}s.",
                    exception);
            }

            operation
                .SetAttribute("messaging.destination", message.Destination)
                .AddSection(new ProtoTraceSection(
                    "Message",
                    ProtoTraceSectionKind.Code,
                    Content: message.Payload is null
                        ? null
                        : ProtoTraceContent.Preview(JsonDiagnosticSanitizer.Sanitize(message.Payload, attachmentOptions)),
                    Language: message.ContentType));
            if (attachmentOptions?.CaptureReceivedPayloads == true)
            {
                Capture(
                    operation,
                    attachmentOptions,
                    $"message-receive-{message.Destination}-{Interlocked.Increment(ref _captureSequence)}-payload",
                    message.Payload,
                    message.ContentType,
                    $"Received payload · {message.Destination}");
            }

            operation.Succeed();
            _context.RecordObservation(new ProtoObservation(
                _broker.Name,
                ProtoMessagingProtocol.Protocol.ResponseObservationKind,
                message.Destination,
                Metadata: new Dictionary<string, object> { ["messaging.system"] = _broker.Name }));
            return message;
        }
        catch (Exception exception)
        {
            operation.Fail(exception);
            RecordFailure(destination, routingKey, exception, cancellationToken);
            throw;
        }
    }

    // A failed publish or await is evidence too: the shared failure record plus the shared guard, with
    // the messaging protocol's own observation kind.
    private void RecordFailure(
        string destination,
        string? routingKey,
        Exception exception,
        CancellationToken cancellationToken)
        => ProtoObservationCapture.TryRecord(_context, ProtoMessagingProtocol.Protocol, () =>
        {
            var metadata = new Dictionary<string, object>
            {
                ["messaging.system"] = _broker.Name,
                ["messaging.destination"] = destination
            };
            if (routingKey is not null)
            {
                metadata["messaging.routing_key"] = routingKey;
            }

            return new ProtoObservation(
                _broker.Name,
                ProtoMessagingProtocol.FailureObservationKind,
                destination,
                Data: ProtoFailureDiagnostics.From(
                    requestUri: null,
                    exception,
                    cancellationToken,
                    _context.TryService<MessagingAttachmentOptions>()),
                Metadata: metadata);
        });

    /// <summary>
    /// Attaches one sanitized payload. The name carries a per-client sequence so repeated captures on the
    /// same destination stay distinct. Capture never fails the operation: a failure is recorded as a
    /// <c>messaging.attachment.failed</c> event, the same rule gRPC and web diagnostics follow.
    /// </summary>
    private void Capture(
        ProtoTraceOperation operation,
        MessagingAttachmentOptions options,
        string name,
        string? payload,
        string? contentType,
        string description)
        => ProtoAttachmentCapture.TryAdd(
            _context,
            operation,
            AttachmentFailure,
            name,
            () => JsonDiagnosticSanitizer.Sanitize(payload ?? string.Empty, options),
            ResolveMediaType(payload, contentType),
            description);

    /// <summary>An explicit content type wins; otherwise Json payloads are <c>application/json</c> and everything else is text.</summary>
    private static string ResolveMediaType(string? payload, string? contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType)) return contentType;
        return JsonDiagnosticSanitizer.LooksLikeJson(payload) ? "application/json" : "text/plain";
    }
}
