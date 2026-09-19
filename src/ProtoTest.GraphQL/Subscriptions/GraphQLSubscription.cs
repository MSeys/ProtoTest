namespace ProtoTest.GraphQL;

using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ProtoTest.Core;
using ProtoTest.GraphQL.Internal;
using ProtoTest.Http;
using ProtoTest.Json;

public sealed class GraphQLSubscription : IAsyncEnumerable<GraphQLResponse>, IAsyncDisposable
{
    private readonly HttpResponseMessage? _response;
    private readonly StreamReader? _reader;
    private readonly WebSocket? _socket;
    private readonly int _maxMessageBytes;
    private readonly Stopwatch _stopwatch;
    private readonly ProtoExecutionContext _context;
    private readonly string _targetName;
    private readonly string _identifier;
    private readonly GraphQLBuiltOperation _operation;
    private readonly ProtoHttpAttachmentOptions? _attachmentOptions;
    private readonly string? _attachmentPrefix;
    private readonly string? _variablesJson;
    private readonly string? _selectedRootField;
    private int _eventNumber;
    private bool _completed;
    private bool _disposed;
    private int _reading;

    internal GraphQLSubscription(
        HttpResponseMessage response,
        Stream stream,
        int maxMessageBytes,
        Stopwatch stopwatch,
        ProtoExecutionContext context,
        string targetName,
        string identifier,
        GraphQLBuiltOperation operation,
        ProtoHttpAttachmentOptions? attachmentOptions,
        string? attachmentPrefix,
        string? variablesJson,
        string? selectedRootField)
    {
        _response = response;
        _reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
        _maxMessageBytes = maxMessageBytes;
        Transport = GraphQLSubscriptionTransport.Sse;
        _stopwatch = stopwatch;
        _context = context;
        _targetName = targetName;
        _identifier = identifier;
        _operation = operation;
        _attachmentOptions = attachmentOptions;
        _attachmentPrefix = attachmentPrefix;
        _variablesJson = variablesJson;
        _selectedRootField = selectedRootField;
    }

    internal GraphQLSubscription(
        WebSocket socket,
        int maxMessageBytes,
        Stopwatch stopwatch,
        ProtoExecutionContext context,
        string targetName,
        string identifier,
        GraphQLBuiltOperation operation,
        ProtoHttpAttachmentOptions? attachmentOptions,
        string? attachmentPrefix,
        string? variablesJson,
        string? selectedRootField)
    {
        _socket = socket;
        _maxMessageBytes = maxMessageBytes;
        Transport = GraphQLSubscriptionTransport.WebSocket;
        _stopwatch = stopwatch;
        _context = context;
        _targetName = targetName;
        _identifier = identifier;
        _operation = operation;
        _attachmentOptions = attachmentOptions;
        _attachmentPrefix = attachmentPrefix;
        _variablesJson = variablesJson;
        _selectedRootField = selectedRootField;
    }

    public bool IsCompleted => _completed;
    public GraphQLSubscriptionTransport Transport { get; }

    public async Task<GraphQLResponse?> NextAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed) return null;
        if (Interlocked.Exchange(ref _reading, 1) != 0)
            throw new InvalidOperationException("Only one subscription result can be read at a time.");

        try
        {
            if (_socket is not null) return await ReadWebSocketResultAsync(cancellationToken);

            var mediaType = _response!.Content.Headers.ContentType?.MediaType;
            if (!string.Equals(mediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
            {
                var payload = await ReadToEndWithinLimitAsync(cancellationToken);
                var response = string.IsNullOrWhiteSpace(payload) ? null : CreateResponse(payload);
                Complete();
                return response;
            }

            while (true)
            {
                var (eventName, data, endOfStream) = await ReadEventAsync(cancellationToken);
                if (string.Equals(eventName, "complete", StringComparison.OrdinalIgnoreCase))
                {
                    Complete();
                    return null;
                }
                if (string.Equals(eventName, "next", StringComparison.OrdinalIgnoreCase) && data.Length > 0)
                {
                    var response = CreateResponse(data);
                    if (endOfStream) Complete();
                    return response;
                }
                if (string.Equals(eventName, "error", StringComparison.OrdinalIgnoreCase))
                {
                    var payload = data.TrimStart().StartsWith('[') ? $"{{\"errors\":{data}}}" : data;
                    var response = CreateResponse(payload);
                    Complete();
                    return response;
                }
                if (endOfStream)
                {
                    Complete();
                    return null;
                }
            }
        }
        finally
        {
            Volatile.Write(ref _reading, 0);
        }
    }

    public async Task<GraphQLResponse> ExpectNextAsync<TShape>(
        TShape expectedShape,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedShape);
        var response = await NextAsync(cancellationToken)
            ?? throw new GraphQLAssertionException("Expected another GraphQL subscription event, but the stream completed.");
        try
        {
            response.ShouldMatchShape(expectedShape);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    public async IAsyncEnumerator<GraphQLResponse> GetAsyncEnumerator(
        CancellationToken cancellationToken = default)
    {
        while (await NextAsync(cancellationToken) is { } response)
            yield return response;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        var wasActive = !_completed;
        _disposed = true;
        Complete();
        _reader?.Dispose();
        _response?.Dispose();
        if (_socket is not null)
        {
            try
            {
                if (wasActive && _socket.State == WebSocketState.Open)
                    await GraphQLWebSocketProtocol.SendAsync(
                        _socket,
                        new { id = "1", type = "complete" },
                        CancellationToken.None);
                if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    await _socket.CloseOutputAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Subscription disposed",
                        CancellationToken.None);
            }
            catch (WebSocketException)
            {
                // Disposal remains best effort when the peer already disconnected.
            }
            finally
            {
                _socket.Dispose();
            }
        }
    }

    private GraphQLResponse CreateResponse(string payload)
    {
        var eventNumber = Interlocked.Increment(ref _eventNumber);
        var statusCode = _response?.StatusCode ?? HttpStatusCode.OK;
        var eventResponse = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/graphql-response+json")
        };
        GraphQLResponse result;
        try
        {
            result = new GraphQLResponse(
                eventResponse,
                payload,
                _stopwatch.Elapsed,
                _context,
                _targetName,
                _identifier,
                _operation,
                _attachmentOptions,
                _attachmentPrefix is null ? null : $"{_attachmentPrefix}-event-{eventNumber:00}",
                selectedRootField: _selectedRootField);
        }
        catch
        {
            eventResponse.Dispose();
            throw;
        }
        if (_attachmentOptions?.CaptureResponses == true)
            _context.AddAttachment(
                $"{_attachmentPrefix}-event-{eventNumber:00}-response",
                JsonDiagnosticSanitizer.Sanitize(
                    GraphQLDocumentRedactor.Redact(payload, _attachmentOptions),
                    _attachmentOptions),
                "application/json",
                _identifier);
        _context.RecordObservation(new ProtoObservation(
            _targetName,
            "graphql.response",
            _identifier,
            new GraphQLResponseData(
                _operation.Type,
                _operation.Name,
                GraphQLDocumentRedactor.Redact(_operation.DocumentText, _attachmentOptions),
                (int)statusCode,
                result.Errors.Count,
                result.Errors.Select(error => error.Code).Where(code => code is not null).Cast<string>().ToArray(),
                _stopwatch.Elapsed,
                _variablesJson)));
        _context.Trace.WriteEvent(
            "graphql.subscription.next",
            $"Subscription event · {eventNumber}",
            "ProtoTest.GraphQL",
            outcome: ProtoTraceOutcome.Succeeded,
            attributes: new Dictionary<string, string?>
            {
                ["graphql.operation.name"] = _operation.Name,
                ["graphql.subscription.event_number"] = eventNumber.ToString(),
                ["graphql.transport"] = Transport == GraphQLSubscriptionTransport.WebSocket ? "websocket" : "sse",
                ["graphql.error.count"] = result.Errors.Count.ToString()
            });
        return result;
    }

    private async Task<(string? EventName, string Data, bool EndOfStream)> ReadEventAsync(
        CancellationToken cancellationToken)
    {
        string? eventName = null;
        var data = new StringBuilder();
        var dataBytes = 0;
        var line = new StringBuilder();
        var buffer = new char[1024];
        var previousWasCarriageReturn = false;
        while (true)
        {
            var read = await _reader!.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
            {
                if (line.Length > 0)
                    ProcessEventLine(line.ToString(), ref eventName, data, ref dataBytes);
                return (eventName, data.ToString(), true);
            }

            for (var index = 0; index < read; index++)
            {
                var character = buffer[index];

                // SSE lines end with CR, LF or CRLF; CR may straddle two reads.
                if (character == '\n' && previousWasCarriageReturn)
                {
                    previousWasCarriageReturn = false;
                    continue;
                }

                previousWasCarriageReturn = false;
                if (character is '\r' or '\n')
                {
                    previousWasCarriageReturn = character == '\r';
                    var lineText = line.ToString();
                    line.Clear();
                    if (lineText.Length == 0)
                    {
                        if (eventName is not null || data.Length > 0)
                            return (eventName, data.ToString(), false);
                        continue;
                    }

                    ProcessEventLine(lineText, ref eventName, data, ref dataBytes);
                    continue;
                }

                line.Append(character);
                // A single frame line is never read past the cap, so an oversized frame cannot be
                // buffered in full before it is rejected.
                if (line.Length > _maxMessageBytes) throw TooLarge();
            }
        }
    }

    private void ProcessEventLine(
        string line,
        ref string? eventName,
        StringBuilder data,
        ref int dataBytes)
    {
        EnsureWithinLimit(Encoding.UTF8.GetByteCount(line));
        if (line[0] == ':') return;
        var separator = line.IndexOf(':');
        var field = separator < 0 ? line : line[..separator];
        var value = separator < 0 ? string.Empty : line[(separator + 1)..].TrimStart(' ');
        if (field == "event")
        {
            eventName = value;
        }
        else if (field == "data")
        {
            dataBytes += (data.Length > 0 ? 1 : 0) + Encoding.UTF8.GetByteCount(value);
            EnsureWithinLimit(dataBytes);
            if (data.Length > 0) data.Append('\n');
            data.Append(value);
        }
    }

    private async Task<string> ReadToEndWithinLimitAsync(CancellationToken cancellationToken)
    {
        var result = new StringBuilder();
        var buffer = new char[1024];
        var totalBytes = 0;
        while (true)
        {
            var read = await _reader!.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            totalBytes += Encoding.UTF8.GetByteCount(buffer, 0, read);
            EnsureWithinLimit(totalBytes);
            result.Append(buffer, 0, read);
        }

        return result.ToString();
    }

    private void EnsureWithinLimit(int bytes)
    {
        if (bytes > _maxMessageBytes) throw TooLarge();
    }

    private GraphQLProtocolException TooLarge()
        => new(
            $"The GraphQL SSE response exceeded the configured limit of {_maxMessageBytes} bytes.",
            string.Empty);

    private async Task<GraphQLResponse?> ReadWebSocketResultAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var message = await GraphQLWebSocketProtocol.ReceiveAsync(
                _socket!,
                _maxMessageBytes,
                cancellationToken);
            if (message is null)
            {
                Complete();
                return null;
            }

            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var typeNode) ? typeNode.GetString() : null;
            if (type == "ping")
            {
                var pong = new Dictionary<string, object?> { ["type"] = "pong" };
                if (root.TryGetProperty("payload", out var pingPayload)) pong["payload"] = pingPayload.Clone();
                await GraphQLWebSocketProtocol.SendAsync(_socket!, pong, cancellationToken);
                continue;
            }
            if (type == "pong") continue;
            if (type == "complete")
            {
                Complete();
                return null;
            }
            if (!root.TryGetProperty("payload", out var payloadNode))
                throw new GraphQLProtocolException(
                    $"The GraphQL WebSocket '{type ?? "<missing>"}' message had no payload.",
                    JsonDiagnosticSanitizer.Sanitize(message, _attachmentOptions));
            if (type == "next") return CreateResponse(payloadNode.GetRawText());
            if (type == "error")
            {
                var payload = payloadNode.ValueKind == JsonValueKind.Array
                    ? $"{{\"errors\":{payloadNode.GetRawText()}}}"
                    : payloadNode.GetRawText();
                var response = CreateResponse(payload);
                Complete();
                return response;
            }
            throw new GraphQLProtocolException(
                $"Unsupported GraphQL WebSocket message type '{type ?? "<missing>"}'.",
                JsonDiagnosticSanitizer.Sanitize(message, _attachmentOptions));
        }
    }

    private void Complete()
    {
        if (_completed) return;
        _completed = true;
        _stopwatch.Stop();
        _context.Trace.WriteEvent(
            "graphql.subscription.complete",
            $"Subscription complete · {_operation.Name ?? "<anonymous>"}",
            "ProtoTest.GraphQL",
            outcome: ProtoTraceOutcome.Succeeded,
            attributes: new Dictionary<string, string?>
            {
                ["graphql.operation.name"] = _operation.Name,
                ["graphql.subscription.event_count"] = _eventNumber.ToString(),
                ["graphql.transport"] = Transport == GraphQLSubscriptionTransport.WebSocket ? "websocket" : "sse",
                ["graphql.duration_ms"] = _stopwatch.Elapsed.TotalMilliseconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
            });
    }
}
