namespace ProtoTest.GraphQL;

using System.Collections;
using System.Net;
using System.Text.Json;
using ProtoTest.Core;
using ProtoTest.GraphQL.Internal;
using ProtoTest.Http;
using ProtoTest.Json;

public sealed class GraphQLResponse : IDisposable
{
    private readonly JsonDocument _document;
    private readonly ProtoExecutionContext _context;
    private readonly string _targetName;
    private readonly string _identifier;
    private readonly ProtoHttpAttachmentOptions? _attachmentOptions;
    private readonly string? _attachmentPrefix;
    private readonly string? _requestTraceId;
    private readonly string? _selectedRootField;
    private int _shapeAssertionSequence;
    private GraphQLAssertions? _should;
    private GraphQLAssertions? _shouldNot;

    internal GraphQLResponse(
        HttpResponseMessage rawResponse,
        string content,
        TimeSpan elapsed,
        ProtoExecutionContext context,
        string targetName,
        string identifier,
        GraphQLBuiltOperation operation,
        ProtoHttpAttachmentOptions? attachmentOptions,
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

    /// <summary>Positive assertions on this response, such as <c>Should.HaveHttpStatus(...)</c>.</summary>
    public GraphQLAssertions Should => _should ??= new GraphQLAssertions(this, negated: false);

    /// <summary>
    /// Assertions that must not hold, such as <c>ShouldNot.HaveHttpStatus(...)</c>. Error and shape
    /// assertions keep their own positive and negative forms.
    /// </summary>
    public GraphQLAssertions ShouldNot => _shouldNot ??= new GraphQLAssertions(this, negated: true);

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

    internal GraphQLResponse AssertHttpStatus(HttpStatusCode expected, bool negated)
    {
        var statusSatisfied = ProtoAssertion.IsSatisfied(HttpStatusCode == expected, negated);
        using var operation = _context.Trace
            .Operation(
                "assert.http.status",
                $"Assert HTTP status · {ProtoAssertion.Describe($"{(int)expected} {expected}", negated)}",
                "ProtoTest.GraphQL")
            .With("expected.status_code", ((int)expected).ToString())
            .With("actual.status_code", ((int)HttpStatusCode).ToString())
            .With("assertion.negated", negated ? "true" : null)
            .Parent(_requestTraceId)
            .Begin();
        operation.AddSection(new ProtoTraceSection(
            "Result",
            ProtoTraceSectionKind.Checks,
            [
                new(
                    "status",
                    ((int)HttpStatusCode).ToString(),
                    statusSatisfied
                        ? null
                        : $"expected {ProtoAssertion.Describe(((int)expected).ToString(), negated)}",
                    statusSatisfied ? ProtoTraceSectionTone.Success : ProtoTraceSectionTone.Error)
            ]));
        try
        {
            if (!statusSatisfied)
            {
                throw new GraphQLAssertionException(
                    $"Expected GraphQL HTTP status {ProtoAssertion.Describe($"{(int)expected} ({expected})", negated)}, " +
                    $"but received {(int)HttpStatusCode} ({HttpStatusCode}).");
            }
            operation.Succeed();
            return this;
        }
        catch (Exception exception)
        {
            operation.Fail(exception);
            throw;
        }
    }

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

    public GraphQLResponse ShouldMatchShape(object expectedShape, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(expectedShape);
        if (!SelectedData.HasValue)
        {
            // A data-less response (errors-only, or "data": null) has nothing to match against; record
            // the failed assertion the same way a mismatch is recorded, then keep the GraphQL-specific
            // failure the docs promise.
            using var operation = _context.Trace
                .Operation("assert.json.shape", "Assert GraphQL data shape", "ProtoTest.GraphQL")
                .With("expected.type", expectedShape.GetType().FullName)
                .With("graphql.operation", _identifier)
                .With("shape.result", "mismatched")
                .Parent(_requestTraceId)
                .Begin();
            var exception = new GraphQLAssertionException(
                "Expected GraphQL data, but the response did not contain data.");
            operation.AddSection(new ProtoTraceSection(
                "Result",
                ProtoTraceSectionKind.Checks,
                [
                    new(
                        "shape",
                        "no data",
                        exception.Message,
                        ProtoTraceSectionTone.Error)
                ]));
            operation.Fail(exception);
            throw exception;
        }

        string? attachmentName = null;
        if (_attachmentOptions?.CaptureExpectedShapes == true)
        {
            var assertionNumber = Interlocked.Increment(ref _shapeAssertionSequence);
            var assertionSuffix = assertionNumber == 1 ? string.Empty : $"-{assertionNumber:00}";
            attachmentName = $"{_attachmentPrefix}-expected-shape{assertionSuffix}";
        }

        ProtoShapeAssertion.Assert(
            new ProtoShapeAssertionContext(
                _context,
                "ProtoTest.GraphQL",
                "Assert GraphQL data shape",
                ParentOperationId: _requestTraceId,
                ExtraAttributes: new Dictionary<string, string?> { ["graphql.operation"] = _identifier },
                CaptureExpectedShape: attachmentName is not null,
                AttachmentName: attachmentName,
                AttachmentDescription: _identifier),
            SelectedData?.GetRawText(),
            expectedShape,
            options,
            _attachmentOptions,
            matched => new ProtoObservation(
                _targetName,
                "graphql.contract.shape",
                _identifier,
                new GraphQLShapeMatchData(_identifier, matched)));

        return this;
    }

    public T? ReadDataAs<T>(JsonSerializerOptions? options = null)
    {
        using var operation = _context.Trace
            .Operation("graphql.response.deserialize", $"Deserialize GraphQL data · {typeof(T).Name}", "ProtoTest.GraphQL")
            .With("target.type", typeof(T).FullName)
            .Parent(_requestTraceId)
            .Begin();
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
        using var operation = _context.Trace
            .Operation(kind, name, "ProtoTest.GraphQL")
            .With(attributes)
            .Parent(_requestTraceId)
            .Begin();
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
            var message = error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var messageNode)
                && messageNode.ValueKind == JsonValueKind.String
                    ? messageNode.GetString() ?? string.Empty
                    : string.Empty;
            var path = error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("path", out var pathNode)
                && pathNode.ValueKind == JsonValueKind.Array
                    ? pathNode.EnumerateArray().Select(ReadPathSegment).ToArray()
                    : [];
            JsonElement? extensions = error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("extensions", out var extensionNode)
                    ? extensionNode.Clone()
                    : null;
            var code = extensions is { ValueKind: JsonValueKind.Object }
                && extensions.Value.TryGetProperty("code", out var codeNode)
                && codeNode.ValueKind == JsonValueKind.String
                    ? codeNode.GetString()
                    : null;
            return new GraphQLError(message, path, code, extensions);
        }).ToArray();
    }

    // A malformed path entry (an object, a float index, a null) must not throw out of error reading.
    private static object ReadPathSegment(JsonElement item)
        => item.ValueKind switch
        {
            JsonValueKind.Number when item.TryGetInt32(out var index) => index,
            JsonValueKind.String => item.GetString() ?? string.Empty,
            _ => item.GetRawText()
        };
}
