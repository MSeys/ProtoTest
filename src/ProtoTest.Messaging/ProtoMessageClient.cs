namespace ProtoTest.Messaging;

using System.Globalization;
using ProtoTest.Core;
using ProtoTest.Json;

/// <summary>
/// The test-side messaging API over the configured broker. Publishes and awaits are traced as
/// <c>messaging.publish</c> and <c>messaging.await</c> operations with the payload as a section, and
/// recorded as observations so coverage can aggregate destinations.
/// </summary>
public sealed class ProtoMessageClient
{
    private readonly ProtoExecutionContext _context;
    private readonly IProtoMessageBroker _broker;
    private readonly IProtoMessageConsumer _consumer;
    private readonly MessagingOptions _options;
    private int _captureSequence;

    internal ProtoMessageClient(
        ProtoExecutionContext context,
        IProtoMessageBroker broker,
        IProtoMessageConsumer consumer,
        MessagingOptions options)
    {
        _context = context;
        _broker = broker;
        _consumer = consumer;
        _options = options;
    }

    /// <summary>Publishes one message, optionally with headers and a content type.</summary>
    public async Task PublishAsync(
        string destination,
        string? payload = null,
        IReadOnlyDictionary<string, string?>? headers = null,
        string? contentType = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        using var operation = _context.Trace
            .Operation("messaging.publish", $"Messaging · publish {destination}", "ProtoTest.Messaging")
            .With("messaging.system", _broker.Name)
            .With("messaging.destination", destination)
            .Begin();
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
            await _broker.PublishAsync(new ProtoMessage(destination, payload, headers, contentType), cancellationToken);
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
                "messaging.publish",
                destination,
                Metadata: new Dictionary<string, object> { ["messaging.system"] = _broker.Name }));
        }
        catch (Exception exception)
        {
            operation.Fail(exception);
            throw;
        }
    }

    /// <summary>
    /// Waits for the first message on <paramref name="destination"/> matching <paramref name="predicate"/>
    /// within the timeout (the configured default when none is given). Failing to arrive is a test
    /// failure, not a sleep.
    /// </summary>
    public async Task<ProtoMessage> AwaitAsync(
        string destination,
        Func<ProtoMessage, bool> predicate,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(predicate);
        var effective = timeout ?? _options.DefaultTimeout;
        using var operation = _context.Trace
            .Operation("messaging.await", $"Messaging · await {destination}", "ProtoTest.Messaging")
            .With("messaging.system", _broker.Name)
            .With("messaging.destination", destination)
            .With("messaging.timeout_ms", effective.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture))
            .Begin();
        var attachmentOptions = _context.TryService<MessagingAttachmentOptions>();
        try
        {
            var message = await _consumer.AwaitAsync(destination, predicate, effective, cancellationToken);
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
                "messaging.receive",
                message.Destination,
                Metadata: new Dictionary<string, object> { ["messaging.system"] = _broker.Name }));
            return message;
        }
        catch (Exception exception)
        {
            operation.Fail(exception);
            throw;
        }
    }

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
    {
        try
        {
            _context.AddAttachment(
                name,
                JsonDiagnosticSanitizer.Sanitize(payload ?? string.Empty, options),
                ResolveMediaType(payload, contentType),
                description);
        }
        catch (Exception exception)
        {
            _context.Trace.WriteEvent(
                "messaging.attachment.failed",
                $"Messaging attachment · {name}",
                "ProtoTest.Messaging",
                outcome: ProtoTraceOutcome.Failed,
                attributes: new Dictionary<string, string?> { ["attachment.name"] = name },
                exception: exception,
                parentId: operation.Id);
        }
    }

    /// <summary>An explicit content type wins; otherwise Json payloads are <c>application/json</c> and everything else is text.</summary>
    private static string ResolveMediaType(string? payload, string? contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType)) return contentType;
        return LooksLikeJson(payload) ? "application/json" : "text/plain";
    }

    private static bool LooksLikeJson(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return false;
        var trimmed = payload.AsSpan().TrimStart();
        return !trimmed.IsEmpty && trimmed[0] is '{' or '[';
    }
}
