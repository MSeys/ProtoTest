namespace ProtoTest.Messaging;

using System.Globalization;
using ProtoTest.Core;

/// <summary>
/// The test-side messaging API over the configured broker. Publishes and awaits are traced as
/// <c>messaging.publish</c> and <c>messaging.await</c> operations with the payload as a section, and
/// recorded as observations so coverage can aggregate destinations.
/// </summary>
public sealed class ProtoMessageClient
{
    private readonly ProtoExecutionContext _context;
    private readonly IProtoMessageBroker _broker;
    private readonly ProtoMessagingOptions _options;
    private readonly long _afterPosition;

    internal ProtoMessageClient(
        ProtoExecutionContext context,
        IProtoMessageBroker broker,
        ProtoMessagingOptions options,
        long afterPosition)
    {
        _context = context;
        _broker = broker;
        _options = options;
        _afterPosition = afterPosition;
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
        if (payload is { Length: > 0 })
        {
            operation.AddSection(new ProtoTraceSection(
                "Message",
                ProtoTraceSectionKind.Code,
                Content: payload,
                Language: contentType));
        }

        try
        {
            await _broker.PublishAsync(new ProtoMessage(destination, payload, headers, contentType), cancellationToken);
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
    /// Waits for the first message matching <paramref name="predicate"/> within the timeout (the
    /// configured default when none is given). Failing to arrive is a test failure, not a sleep.
    /// </summary>
    public async Task<ProtoMessage> AwaitAsync(
        Func<ProtoMessage, bool> predicate,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var effective = timeout ?? _options.DefaultTimeout;
        using var operation = _context.Trace
            .Operation("messaging.await", $"Messaging · await on {_broker.Name}", "ProtoTest.Messaging")
            .With("messaging.system", _broker.Name)
            .With("messaging.timeout_ms", effective.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture))
            .Begin();
        try
        {
            var message = await _broker.AwaitAsync(predicate, effective, _afterPosition, cancellationToken);
            operation
                .SetAttribute("messaging.destination", message.Destination)
                .AddSection(new ProtoTraceSection(
                    "Message",
                    ProtoTraceSectionKind.Code,
                    Content: message.Payload,
                    Language: message.ContentType));
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
}
