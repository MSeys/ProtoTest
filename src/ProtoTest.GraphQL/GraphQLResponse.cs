namespace ProtoTest.GraphQL;

using System.Collections;
using System.Net;
using System.Text.Json;
using ProtoTest.Core;
using ProtoTest.GraphQL.Internal;
using ProtoTest.Json;

public sealed class GraphQLResponse : IDisposable
{
    private readonly JsonDocument _document;
    private readonly ProtoExecutionContext _context;
    private readonly string _targetName;
    private readonly string _identifier;
    private readonly GraphQLAttachmentOptions? _attachmentOptions;
    private readonly string? _attachmentPrefix;
    private readonly string? _requestTraceId;
    private readonly string? _selectedRootField;

    internal GraphQLResponse(
        HttpResponseMessage rawResponse,
        string content,
        TimeSpan elapsed,
        ProtoExecutionContext context,
        string targetName,
        string identifier,
        GraphQLBuiltOperation operation,
        GraphQLAttachmentOptions? attachmentOptions,
        string? attachmentPrefix,
        string? requestTraceId = null,
        string? selectedRootField = null)
    {
        RawResponse = rawResponse;
        Content = content;
        ElapsedTime = elapsed;
        _context = context;
        _targetName = targetName;
        _identifier = identifier;
        _attachmentOptions = attachmentOptions;
        _attachmentPrefix = attachmentPrefix;
        _requestTraceId = requestTraceId;
        _selectedRootField = selectedRootField;
        try
        {
            _document = JsonDocument.Parse(content);
        }
        catch (JsonException exception)
        {
            throw new GraphQLProtocolException(
                "The server did not return a valid JSON GraphQL response.",
                JsonDiagnosticSanitizer.Sanitize(content, attachmentOptions),
                exception);
        }

        if (_document.RootElement.ValueKind != JsonValueKind.Object ||
            (!_document.RootElement.TryGetProperty("data", out _) && !_document.RootElement.TryGetProperty("errors", out _)))
        {
            _document.Dispose();
            throw new GraphQLProtocolException(
                "The response did not contain a GraphQL 'data' or 'errors' member.",
                JsonDiagnosticSanitizer.Sanitize(content, attachmentOptions));
        }

        Errors = ReadErrors(_document.RootElement);
    }

    public HttpResponseMessage RawResponse { get; }
    public HttpStatusCode HttpStatusCode => RawResponse.StatusCode;
    public string Content { get; }
    public TimeSpan ElapsedTime { get; }
    public IReadOnlyList<GraphQLError> Errors { get; }
    public bool HasErrors => Errors.Count > 0;
    public bool HasData => _document.RootElement.TryGetProperty("data", out var data) && data.ValueKind != JsonValueKind.Null;
    public JsonElement? Data => _document.RootElement.TryGetProperty("data", out var data) ? data.Clone() : null;
    /// <summary>The selected root-field value for shape-driven operations; otherwise the complete data object.</summary>
    public JsonElement? SelectedData
    {
        get
        {
            if (!Data.HasValue) return null;
            var data = Data.Value;
            return _selectedRootField is not null && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty(_selectedRootField, out var selected)
                    ? selected.Clone()
                    : data;
        }
    }
    public JsonElement? Extensions => _document.RootElement.TryGetProperty("extensions", out var extensions) ? extensions.Clone() : null;

    public GraphQLResponse ShouldHaveHttpStatus(HttpStatusCode expected)
        => Assert(
            "assert.graphql.http_status",
            $"Assert HTTP status · {(int)expected} {expected}",
            new Dictionary<string, string?>
            {
                ["expected.status_code"] = ((int)expected).ToString(),
                ["actual.status_code"] = ((int)HttpStatusCode).ToString()
            },
            () =>
            {
                if (HttpStatusCode != expected)
                    throw new GraphQLAssertionException($"Expected GraphQL HTTP status {(int)expected} ({expected}), but received {(int)HttpStatusCode} ({HttpStatusCode}).");
            });

    public GraphQLResponse ShouldHaveNoErrors()
        => Assert(
            "assert.graphql.no_errors",
            "Assert no GraphQL errors",
            new Dictionary<string, string?> { ["actual.error_count"] = Errors.Count.ToString() },
            () =>
            {
                if (HasErrors)
                    throw new GraphQLAssertionException($"Expected no GraphQL errors, but received {Errors.Count}: {string.Join("; ", Errors.Select(e => e.Message))}");
            });

    public GraphQLResponse ShouldHaveErrors()
        => Assert(
            "assert.graphql.has_errors",
            "Assert GraphQL has errors",
            new Dictionary<string, string?> { ["actual.error_count"] = Errors.Count.ToString() },
            () =>
            {
                if (!HasErrors) throw new GraphQLAssertionException("Expected at least one GraphQL error, but the response contained none.");
            });

    public GraphQLResponse ShouldHaveError(string code)
        => Assert(
            "assert.graphql.error_code",
            $"Assert GraphQL error · {code}",
            new Dictionary<string, string?>
            {
                ["expected.error_code"] = code,
                ["actual.error_codes"] = string.Join(", ", Errors.Select(error => error.Code ?? "<none>"))
            },
            () =>
            {
                if (!Errors.Any(error => string.Equals(error.Code, code, StringComparison.OrdinalIgnoreCase)))
                    throw new GraphQLAssertionException($"Expected a GraphQL error with code '{code}', but found: {string.Join(", ", Errors.Select(e => e.Code ?? "<none>"))}.");
            });

    public GraphQLResponse ShouldMatchData(object expectedShape, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(expectedShape);
        var expectedShapeJson = JsonDiagnosticSanitizer.Serialize(expectedShape, _attachmentOptions);
        var actualShapeJson = SelectedData.HasValue
            ? JsonDiagnosticSanitizer.Sanitize(SelectedData.Value.GetRawText(), _attachmentOptions)
            : null;
        using var operation = _context.Trace.StartOperation(
            "assert.graphql.data_shape",
            "Assert GraphQL data shape",
            "ProtoTest.GraphQL",
            attributes: new Dictionary<string, string?>
            {
                ["expected.type"] = expectedShape.GetType().FullName,
                ["shape.expected"] = expectedShapeJson,
                ["shape.actual"] = actualShapeJson,
                ["graphql.operation"] = _identifier
            },
            parentId: _requestTraceId);
        try
        {
            if (!SelectedData.HasValue) throw new GraphQLAssertionException("Expected GraphQL data, but the response did not contain data.");
            if (_attachmentOptions?.CaptureExpectedShapes == true)
                _context.AddAttachment(
                    $"{_attachmentPrefix}-expected-shape",
                    JsonDiagnosticSanitizer.Sanitize(JsonSerializer.Serialize(expectedShape), _attachmentOptions),
                    "application/json",
                    _identifier);
            var matched = GraphQLShapeMatcher.AssertMatch(SelectedData.Value, expectedShape, options);
            operation.SetAttribute("matched.property_count", matched.Count.ToString());
            operation.SetAttribute("matched.properties", string.Join(", ", matched));
            operation.SetAttribute("shape.matches", JsonDiagnosticSanitizer.Serialize(matched, _attachmentOptions));
            operation.SetAttribute("shape.result", "matched");
            _context.RecordObservation(new ProtoObservation(
                _targetName,
                "graphql.contract.shape",
                _identifier,
                new GraphQLShapeMatchData(_identifier, matched)));
            operation.Succeed();
            return this;
        }
        catch (GraphQLShapeMismatchException exception)
        {
            operation.SetAttribute("shape.result", "mismatched");
            operation.SetAttribute("shape.matches", JsonDiagnosticSanitizer.Serialize(exception.MatchedProperties, _attachmentOptions));
            operation.SetAttribute("shape.mismatches", JsonDiagnosticSanitizer.Serialize(exception.Mismatches, _attachmentOptions));
            operation.SetAttribute("shape.mismatch_count", exception.Mismatches.Count.ToString());
            operation.Fail(exception);
            throw;
        }
        catch (Exception exception)
        {
            operation.Fail(exception);
            throw;
        }
    }

    public T? ReadDataAs<T>(JsonSerializerOptions? options = null)
    {
        using var operation = _context.Trace.StartOperation(
            "graphql.response.deserialize",
            $"Deserialize GraphQL data · {typeof(T).Name}",
            "ProtoTest.GraphQL",
            attributes: new Dictionary<string, string?> { ["target.type"] = typeof(T).FullName },
            parentId: _requestTraceId);
        try
        {
            var result = SelectedData.HasValue
                ? SelectedData.Value.Deserialize<T>(options ?? new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                : default;
            operation.Succeed();
            return result;
        }
        catch (Exception exception)
        {
            operation.Fail(exception);
            throw;
        }
    }

    public void Dispose()
    {
        _document.Dispose();
        RawResponse.Dispose();
    }

    private GraphQLResponse Assert(
        string kind,
        string name,
        IReadOnlyDictionary<string, string?> attributes,
        Action assertion)
    {
        using var operation = _context.Trace.StartOperation(
            kind,
            name,
            "ProtoTest.GraphQL",
            attributes: attributes,
            parentId: _requestTraceId);
        try
        {
            assertion();
            operation.Succeed();
            return this;
        }
        catch (Exception exception)
        {
            operation.Fail(exception);
            throw;
        }
    }

    private static IReadOnlyList<GraphQLError> ReadErrors(JsonElement root)
    {
        if (!root.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Array) return [];
        return errors.EnumerateArray().Select(error =>
        {
            var message = error.TryGetProperty("message", out var messageNode) ? messageNode.GetString() ?? string.Empty : string.Empty;
            var path = error.TryGetProperty("path", out var pathNode) && pathNode.ValueKind == JsonValueKind.Array
                ? pathNode.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.Number ? (object)item.GetInt32() : item.GetString() ?? string.Empty).ToArray()
                : [];
            JsonElement? extensions = error.TryGetProperty("extensions", out var extensionNode) ? extensionNode.Clone() : null;
            var code = extensions.HasValue && extensions.Value.ValueKind == JsonValueKind.Object && extensions.Value.TryGetProperty("code", out var codeNode)
                ? codeNode.GetString()
                : null;
            return new GraphQLError(message, path, code, extensions);
        }).ToArray();
    }
}

public sealed record GraphQLError(string Message, IReadOnlyList<object> Path, string? Code, JsonElement? Extensions);
public sealed record GraphQLShapeMatchData(string RequestIdentifier, IReadOnlyList<string> MatchedProperties);

public sealed class GraphQLAssertionException(string message) : ProtoAssertionException(message);

public sealed class GraphQLProtocolException : Exception
{
    public GraphQLProtocolException(string message, string responseContent, Exception? innerException = null)
        : base(message, innerException) => ResponseContent = responseContent;
    public string ResponseContent { get; }
}
