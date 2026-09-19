namespace ProtoTest.Rest;

using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.Json;
using ProtoTest.Rest.Exceptions;
using ProtoTest.Rest.Internal;
using System.Dynamic;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

public sealed class RestResponse : IDisposable, IProtoBinaryContent
{
    private readonly ProtoExecutionContext? _context;
    private readonly string? _targetName;
    private readonly string? _routeIdentifier;
    private readonly ProtoHttpAttachmentOptions? _attachmentOptions;
    private readonly string? _attachmentPrefix;
    private readonly string? _requestTraceId;
    private int _shapeAssertionSequence;
    private RestAssertions? _should;
    private RestAssertions? _shouldNot;

    private static readonly JsonSerializerOptions DefaultJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    internal RestResponse(
        HttpResponseMessage rawResponse,
        string content,
        TimeSpan elapsedTime,
        ProtoExecutionContext? context = null,
        string? targetName = null,
        string? routeIdentifier = null,
        ProtoHttpAttachmentOptions? attachmentOptions = null,
        string? attachmentPrefix = null,
        ReadOnlyMemory<byte>? contentBytes = null,
        string? requestTraceId = null)
    {
        RawResponse = rawResponse ?? throw new ArgumentNullException(nameof(rawResponse));
        Content = content ?? string.Empty;
        ElapsedTime = elapsedTime;
        ContentBytes = contentBytes ?? System.Text.Encoding.UTF8.GetBytes(Content);
        _context = context;
        _targetName = targetName;
        _routeIdentifier = routeIdentifier;
        _attachmentOptions = attachmentOptions;
        _attachmentPrefix = attachmentPrefix;
        _requestTraceId = requestTraceId;
    }

    public HttpResponseMessage RawResponse { get; }
    public HttpStatusCode StatusCode => RawResponse.StatusCode;

    /// <summary>Positive assertions on this response, such as <c>Should.HaveHttpStatus(...)</c>.</summary>
    public RestAssertions Should => _should ??= new RestAssertions(this, negated: false);

    /// <summary>
    /// Assertions that must not hold, such as <c>ShouldNot.HaveHttpStatus(...)</c>. Shape assertions
    /// stay on this response because a negated shape match is not meaningful.
    /// </summary>
    public RestAssertions ShouldNot => _shouldNot ??= new RestAssertions(this, negated: true);

    public bool IsSuccessStatusCode => RawResponse.IsSuccessStatusCode;
    public HttpResponseHeaders Headers => RawResponse.Headers;
    public HttpContentHeaders ContentHeaders => RawResponse.Content.Headers;
    public TimeSpan ElapsedTime { get; }
    public string Content { get; }
    public ReadOnlyMemory<byte> ContentBytes { get; }

    string? IProtoBinaryContent.MediaType => ContentHeaders.ContentType?.MediaType;

    ReadOnlyMemory<byte> IProtoBinaryContent.Content => ContentBytes;

    string? IProtoBinaryContent.FileName => RawResponse.Content.Headers.ContentDisposition is { } disposition
        ? (disposition.FileNameStar ?? disposition.FileName)?.Trim('"')
        : null;

    /// <summary>
    /// Releases the underlying HTTP response. Callers own a returned <see cref="RestResponse"/>
    /// and should dispose it when access to <see cref="RawResponse"/> is no longer required.
    /// </summary>
    public void Dispose() => RawResponse.Dispose();

    public T? ReadAsJson<T>(JsonSerializerOptions? options = null)
    {
        try
        {
            return string.IsNullOrWhiteSpace(Content)
                ? default
                : JsonSerializer.Deserialize<T>(Content, options ?? DefaultJsonOptions);
        }
        catch (Exception exception)
        {
            _context?.Trace.WriteEvent(
                "http.response.deserialize",
                $"Deserialize response · {typeof(T).Name}",
                "ProtoTest.Rest",
                outcome: ProtoTraceOutcome.Failed,
                attributes: new Dictionary<string, string?>
                {
                    ["target.type"] = typeof(T).FullName,
                    ["content.length"] = ContentBytes.Length.ToString()
                },
                exception: exception,
                parentId: _requestTraceId);
            throw;
        }
    }

    /// <summary>
    /// Deserializes the body into an anonymous type described by example, e.g.
    /// <c>ReadAsAnonymous(new { id = 0, status = "" })</c>.
    /// </summary>
    /// <param name="anonymousTypeDefinition">
    /// Only its type is used, so the compiler can infer the anonymous type; its values are ignored.
    /// </param>
    /// <param name="options">Serializer options; property names are case-insensitive by default.</param>
    public T? ReadAsAnonymous<T>(T anonymousTypeDefinition, JsonSerializerOptions? options = null)
    {
        return ReadAsJson<T>(options);
    }

    public dynamic? ReadAsDynamic()
    {
        if (string.IsNullOrWhiteSpace(Content))
            return null;

        using var doc = JsonDocument.Parse(Content);
        return ConvertJsonElement(doc.RootElement.Clone());
    }

    internal RestResponse AssertHttpStatus(HttpStatusCode expectedStatusCode, bool negated)
    {
        var statusSatisfied = ProtoAssertion.IsSatisfied(StatusCode == expectedStatusCode, negated);
        using var operation = _context is null
            ? null
            : _context.Trace
                .Operation(
                    "assert.http.status",
                    $"Assert status · {ProtoAssertion.Describe($"{(int)expectedStatusCode} {expectedStatusCode}", negated)}",
                    "ProtoTest.Rest")
                .With("expected.status_code", ((int)expectedStatusCode).ToString())
                .With("actual.status_code", ((int)StatusCode).ToString())
                .With("assertion.negated", negated ? "true" : null)
                .With("request.identifier", _routeIdentifier)
                .Parent(_requestTraceId)
                .Begin();
        operation?.AddSection(new ProtoTraceSection(
            "Result",
            ProtoTraceSectionKind.Checks,
            [
                new(
                    "status",
                    ((int)StatusCode).ToString(),
                    statusSatisfied
                        ? null
                        : $"expected {ProtoAssertion.Describe(((int)expectedStatusCode).ToString(), negated)}",
                    statusSatisfied ? ProtoTraceSectionTone.Success : ProtoTraceSectionTone.Error)
            ]));
        try
        {
            if (!statusSatisfied)
            {
                // The failure message must not exceed either limit: the response section applies even
                // when the protocol never opted into attachment capture, and the attachment options
                // keep their own redaction rules when capture is on.
                var diagnosticBody = ProtoHttpDiagnosticSanitizer.SanitizeBody(
                    Content,
                    ResolveStatusDiagnosticOptions());
                throw new RestStatusAssertionException(expectedStatusCode, StatusCode, diagnosticBody, negated);
            }
            operation?.Succeed();
            return this;
        }
        catch (Exception exception)
        {
            operation?.Fail(exception);
            throw;
        }
    }

    private ProtoHttpAttachmentOptions ResolveStatusDiagnosticOptions()
    {
        // The resolved ProtoTest:Rest:Responses section bounds the failure body even when the protocol
        // never opted into attachment capture; attachment options, when present, keep their own
        // redaction rules and may tighten the limit further.
        var responseLimit = _context?.ResolveResponseOptions(ProtoRestBuilder.ProtocolName).MaxDiagnosticBodyLength
            ?? new ProtoHttpResponseOptions().MaxDiagnosticBodyLength;
        if (_attachmentOptions is null)
            return new ProtoHttpAttachmentOptions { MaxDiagnosticBodyLength = responseLimit };
        if (responseLimit >= _attachmentOptions.MaxDiagnosticBodyLength)
            return _attachmentOptions;

        return new ProtoHttpAttachmentOptions
        {
            RedactSensitiveData = _attachmentOptions.RedactSensitiveData,
            SensitiveJsonProperties = [.. _attachmentOptions.SensitiveJsonProperties],
            MaxDiagnosticBodyLength = responseLimit
        };
    }

    public RestResponse ShouldMatchShape(object expectedShape, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(expectedShape);
        string? attachmentName = null;
        if (_context is not null && _attachmentOptions?.CaptureExpectedShapes == true)
        {
            var assertionNumber = Interlocked.Increment(ref _shapeAssertionSequence);
            var assertionSuffix = assertionNumber == 1 ? string.Empty : $"-{assertionNumber:00}";
            attachmentName = $"{_attachmentPrefix}-expected-shape{assertionSuffix}";
        }

        ProtoShapeAssertion.Assert(
            new ProtoShapeAssertionContext(
                _context,
                "ProtoTest.Rest",
                "Assert response shape",
                ParentOperationId: _requestTraceId,
                ExtraAttributes: new Dictionary<string, string?>
                {
                    ["actual.media_type"] = RawResponse.Content.Headers.ContentType?.MediaType,
                    ["request.identifier"] = _routeIdentifier
                },
                CaptureExpectedShape: attachmentName is not null,
                AttachmentName: attachmentName,
                AttachmentDescription: _routeIdentifier),
            Content,
            expectedShape,
            options,
            _attachmentOptions,
            matched => _context is not null && !string.IsNullOrEmpty(_targetName) && !string.IsNullOrEmpty(_routeIdentifier)
                ? new ProtoObservation(
                    TargetName: _targetName,
                    Kind: "http.contract.shape",
                    Identifier: _routeIdentifier,
                    Data: new RestShapeMatchData(
                        RequestIdentifier: _routeIdentifier,
                        MatchedProperties: matched,
                        TargetType: expectedShape.GetType(),
                        StatusCode: (int)StatusCode))
                : null);

        return this;
    }

    public byte[] ReadAsBytes() => ContentBytes.ToArray();

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .Aggregate(new ExpandoObject() as IDictionary<string, object?>, (acc, prop) =>
                {
                    acc[prop.Name] = ConvertJsonElement(prop.Value);
                    return acc;
                }),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }
}
