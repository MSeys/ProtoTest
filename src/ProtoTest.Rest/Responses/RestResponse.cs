namespace ProtoTest.Rest;

using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.Json;
using ProtoTest.Rest.Exceptions;
using ProtoTest.Rest.Internal;
using System.Dynamic;
using System.Collections;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

public sealed class RestResponse : IDisposable
{
    private readonly ProtoExecutionContext? _context;
    private readonly string? _targetName;
    private readonly string? _routeIdentifier;
    private readonly RestAttachmentOptions? _attachmentOptions;
    private readonly string? _attachmentPrefix;
    private readonly string? _requestTraceId;
    private int _shapeAssertionSequence;

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
        RestAttachmentOptions? attachmentOptions = null,
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
    public bool IsSuccessStatusCode => RawResponse.IsSuccessStatusCode;
    public HttpResponseHeaders Headers => RawResponse.Headers;
    public HttpContentHeaders ContentHeaders => RawResponse.Content.Headers;
    public TimeSpan ElapsedTime { get; }
    public string Content { get; }
    public ReadOnlyMemory<byte> ContentBytes { get; }

    /// <summary>
    /// Releases the underlying HTTP response. Callers own a returned <see cref="RestResponse"/>
    /// and should dispose it when access to <see cref="RawResponse"/> is no longer required.
    /// </summary>
    public void Dispose() => RawResponse.Dispose();

    public T? ReadAsJson<T>(JsonSerializerOptions? options = null)
    {
        using var operation = _context is null
            ? null
            : _context.Trace
                .Operation("http.response.deserialize", $"Deserialize response · {typeof(T).Name}", "ProtoTest.Rest")
                .With("target.type", typeof(T).FullName)
                .With("content.length", ContentBytes.Length.ToString())
                .Parent(_requestTraceId)
                .Begin();
        try
        {
            var result = string.IsNullOrWhiteSpace(Content)
                ? default
                : JsonSerializer.Deserialize<T>(Content, options ?? DefaultJsonOptions);
            operation?.Succeed();
            return result;
        }
        catch (Exception exception)
        {
            operation?.Fail(exception);
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

    public RestResponse ShouldHaveHttpStatus(HttpStatusCode expectedStatusCode)
    {
        using var operation = _context is null
            ? null
            : _context.Trace
                .Operation("assert.http.status", $"Assert status · {(int)expectedStatusCode} {expectedStatusCode}", "ProtoTest.Rest")
                .With("expected.status_code", ((int)expectedStatusCode).ToString())
                .With("actual.status_code", ((int)StatusCode).ToString())
                .With("request.identifier", _routeIdentifier)
                .Parent(_requestTraceId)
                .Begin();
        try
        {
            if (StatusCode != expectedStatusCode)
            {
                var diagnosticBody = ProtoHttpDiagnosticSanitizer.SanitizeBody(
                    Content,
                    _attachmentOptions);
                throw new RestStatusAssertionException(expectedStatusCode, StatusCode, diagnosticBody);
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

    public RestResponse ShouldMatchShape(object expectedShape, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(expectedShape);
        var expectedShapeJson = JsonDiagnosticSanitizer.Serialize(DescribeExpectedValue(expectedShape), _attachmentOptions);
        var actualShapeJson = ProtoHttpDiagnosticSanitizer.SanitizeBody(
            Content,
            _attachmentOptions);
        using var operation = _context is null
            ? null
            : _context.Trace
                .Operation("assert.json.shape", "Assert response shape", "ProtoTest.Rest")
                .With("expected.type", expectedShape.GetType().FullName)
                .With("shape.expected", expectedShapeJson)
                .With("shape.actual", actualShapeJson)
                .With("actual.media_type", RawResponse.Content.Headers.ContentType?.MediaType)
                .With("request.identifier", _routeIdentifier)
                .Parent(_requestTraceId)
                .Begin();

        try
        {
            if (_context is not null && _attachmentOptions?.CaptureExpectedShapes == true)
            {
                var assertionNumber = Interlocked.Increment(ref _shapeAssertionSequence);
                var assertionSuffix = assertionNumber == 1 ? string.Empty : $"-{assertionNumber:00}";
                _context.AddAttachment(
                    $"{_attachmentPrefix}-expected-shape{assertionSuffix}",
                    ProtoHttpDiagnosticSanitizer.SanitizeBody(
                        expectedShapeJson,
                        _attachmentOptions),
                    "application/json",
                    _routeIdentifier);
            }

            var matchedProps = JsonShapeMatcher.AssertMatch(Content, expectedShape, options);
            operation?.SetAttribute("matched.property_count", matchedProps.Count.ToString());
            operation?.SetAttribute("matched.properties", string.Join(", ", matchedProps));
            operation?.SetAttribute("shape.matches", JsonDiagnosticSanitizer.Serialize(matchedProps, _attachmentOptions));
            operation?.SetAttribute("shape.result", "matched");

            // Record the shape-match observation when an execution context is available.
            if (_context != null && !string.IsNullOrEmpty(_targetName) && !string.IsNullOrEmpty(_routeIdentifier))
            {
                _context.RecordObservation(new ProtoObservation(
                    TargetName: _targetName,
                    Kind: "http.contract.shape",
                    Identifier: _routeIdentifier,
                    Data: new RestShapeMatchData(
                        RequestIdentifier: _routeIdentifier,
                        MatchedProperties: matchedProps,
                        TargetType: expectedShape.GetType(),
                        StatusCode: (int)StatusCode
                    )
                ));
            }

            operation?.Succeed();
            return this;
        }
        catch (JsonShapeMismatchException exception)
        {
            operation?.SetAttribute("shape.result", "mismatched");
            operation?.SetAttribute("shape.matches", JsonDiagnosticSanitizer.Serialize(exception.MatchedProperties, _attachmentOptions));
            operation?.SetAttribute("shape.mismatches", JsonDiagnosticSanitizer.Serialize(exception.Mismatches, _attachmentOptions));
            operation?.SetAttribute("shape.mismatch_count", exception.Mismatches.Count.ToString());
            operation?.Fail(exception);
            throw;
        }
        catch (Exception exception)
        {
            operation?.Fail(exception);
            throw;
        }
    }

    public byte[] ReadAsBytes() => ContentBytes.ToArray();

    private static object? DescribeExpectedValue(object? expected)
    {
        if (expected is null)
        {
            return null;
        }

        if (expected is IJsonValueMatcher matcher)
        {
            return $"constraint: {matcher.Description}";
        }

        var type = expected.GetType();
        if (type.IsPrimitive || type.IsEnum || expected is string or decimal or DateTime or DateTimeOffset or Guid)
        {
            return expected;
        }

        if (expected is IEnumerable values)
        {
            return values.Cast<object?>().Select(DescribeExpectedValue).ToArray();
        }

        return type.GetProperties()
            .Where(property => property.GetIndexParameters().Length == 0)
            .ToDictionary(
                property => property.Name,
                property => DescribeExpectedValue(property.GetValue(expected)));
    }

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
