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

    internal GraphQLResponse(
        HttpResponseMessage rawResponse,
        string content,
        TimeSpan elapsed,
        ProtoExecutionContext context,
        string targetName,
        string identifier,
        GraphQLBuiltOperation operation,
        GraphQLAttachmentOptions? attachmentOptions,
        string? attachmentPrefix)
    {
        RawResponse = rawResponse;
        Content = content;
        ElapsedTime = elapsed;
        _context = context;
        _targetName = targetName;
        _identifier = identifier;
        _attachmentOptions = attachmentOptions;
        _attachmentPrefix = attachmentPrefix;
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
    public JsonElement? Extensions => _document.RootElement.TryGetProperty("extensions", out var extensions) ? extensions.Clone() : null;

    public GraphQLResponse ShouldHaveHttpStatus(HttpStatusCode expected)
    {
        if (HttpStatusCode != expected)
            throw new GraphQLAssertionException($"Expected GraphQL HTTP status {(int)expected} ({expected}), but received {(int)HttpStatusCode} ({HttpStatusCode}).");
        return this;
    }

    public GraphQLResponse ShouldHaveNoErrors()
    {
        if (HasErrors)
            throw new GraphQLAssertionException($"Expected no GraphQL errors, but received {Errors.Count}: {string.Join("; ", Errors.Select(e => e.Message))}");
        return this;
    }

    public GraphQLResponse ShouldHaveErrors()
    {
        if (!HasErrors) throw new GraphQLAssertionException("Expected at least one GraphQL error, but the response contained none.");
        return this;
    }

    public GraphQLResponse ShouldHaveError(string code)
    {
        if (!Errors.Any(error => string.Equals(error.Code, code, StringComparison.OrdinalIgnoreCase)))
            throw new GraphQLAssertionException($"Expected a GraphQL error with code '{code}', but found: {string.Join(", ", Errors.Select(e => e.Code ?? "<none>"))}.");
        return this;
    }

    public GraphQLResponse ShouldMatchData(object expectedShape, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(expectedShape);
        if (!Data.HasValue) throw new GraphQLAssertionException("Expected GraphQL data, but the response did not contain data.");
        if (_attachmentOptions?.CaptureExpectedShapes == true)
            _context.AddAttachment(
                $"{_attachmentPrefix}-expected-shape",
                JsonDiagnosticSanitizer.Sanitize(JsonSerializer.Serialize(expectedShape), _attachmentOptions),
                "application/json",
                _identifier);
        var matched = GraphQLShapeMatcher.AssertMatch(Data.Value, expectedShape, options);
        _context.RecordObservation(new ProtoObservation(
            _targetName,
            "graphql.contract.shape",
            _identifier,
            new GraphQLShapeMatchData(_identifier, matched)));
        return this;
    }

    public T? ReadDataAs<T>(JsonSerializerOptions? options = null)
        => Data.HasValue ? Data.Value.Deserialize<T>(options ?? new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) : default;

    public void Dispose()
    {
        _document.Dispose();
        RawResponse.Dispose();
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
