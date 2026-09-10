namespace ProtoTest.Rest;

using ProtoTest.Core;
using ProtoTest.Rest.Exceptions;
using ProtoTest.Rest.Internal;
using ProtoTest.Rest.Matching;
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
        ReadOnlyMemory<byte>? contentBytes = null)
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
        if (string.IsNullOrWhiteSpace(Content))
            return default;

        return JsonSerializer.Deserialize<T>(Content, options ?? DefaultJsonOptions);
    }

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

    public RestResponse ShouldHaveStatus(HttpStatusCode expectedStatusCode)
    {
        if (StatusCode != expectedStatusCode)
        {
            var diagnosticBody = RestDiagnosticSanitizer.SanitizeBody(
                Content,
                RawResponse.Content.Headers.ContentType?.MediaType,
                _attachmentOptions);
            throw new RestStatusAssertionException(expectedStatusCode, StatusCode, diagnosticBody);
        }
        return this;
    }

    public RestResponse ShouldMatchShape(object expectedShape, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(expectedShape);

        if (_context is not null && _attachmentOptions?.CaptureExpectedShapes == true)
        {
            var assertionNumber = Interlocked.Increment(ref _shapeAssertionSequence);
            var assertionSuffix = assertionNumber == 1 ? string.Empty : $"-{assertionNumber:00}";
            var expectedShapeJson = JsonSerializer.Serialize(
                DescribeExpectedValue(expectedShape),
                new JsonSerializerOptions { WriteIndented = true });
            _context.AddAttachment(
                $"{_attachmentPrefix}-expected-shape{assertionSuffix}",
                RestDiagnosticSanitizer.SanitizeBody(
                    expectedShapeJson,
                    "application/json",
                    _attachmentOptions),
                "application/json",
                _routeIdentifier);
        }

        var matchedProps = ShapeMatcher.AssertMatch(Content, expectedShape, options);

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
                    TargetType: expectedShape.GetType()
                )
            ));
        }

        return this;
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
