namespace ProtoTest.GraphQL;

using System.Collections;
using System.Net;
using System.Text.Json;
using ProtoTest.Core;
using ProtoTest.GraphQL.Internal;
using ProtoTest.Http;
using ProtoTest.Json;

public sealed class GraphQLResponse : ProtoHttpResponse
{
    private readonly JsonDocument _document;
    private readonly string? _selectedRootField;
    private GraphQLShouldAssertions? _should;
    private GraphQLAssertions? _shouldNot;

    internal GraphQLResponse(
        HttpResponseMessage rawResponse,
        string content,
        TimeSpan elapsed,
        ProtoHttpResponseContext context,
        GraphQLBuiltOperation operation,
        string? selectedRootField = null)
        : base(rawResponse, content, elapsed, context)
    {
        _selectedRootField = selectedRootField;
        try
        {
            _document = JsonDocument.Parse(content);
        }
        catch (JsonException exception)
        {
            throw new GraphQLProtocolException(
                "The server did not return a valid JSON GraphQL response.",
                JsonDiagnosticSanitizer.Sanitize(content, context.AttachmentOptions),
                exception);
        }

        if (_document.RootElement.ValueKind != JsonValueKind.Object ||
            (!_document.RootElement.TryGetProperty("data", out _) && !_document.RootElement.TryGetProperty("errors", out _)))
        {
            _document.Dispose();
            throw new GraphQLProtocolException(
                "The response did not contain a GraphQL 'data' or 'errors' member.",
                JsonDiagnosticSanitizer.Sanitize(content, context.AttachmentOptions));
        }

        Errors = ReadErrors(_document.RootElement);
    }

    /// <summary>
    /// The positive assertions on this response, such as <c>Should.HaveHttpStatus(...)</c> and
    /// <c>Should.MatchShape(...)</c>.
    /// </summary>
    public GraphQLShouldAssertions Should => _should ??= new GraphQLShouldAssertions(this);

    /// <summary>
    /// Assertions that must not hold, such as <c>ShouldNot.HaveHttpStatus(...)</c> or
    /// <c>ShouldNot.HaveErrors()</c>. A negated shape match is not meaningful, so shape lives on
    /// <see cref="Should"/>.
    /// </summary>
    public GraphQLAssertions ShouldNot => _shouldNot ??= new GraphQLAssertions(this, negated: true);

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
        AssertStatus(
            ProtoGraphQLBuilder.Protocol.TraceSource,
            expected,
            negated,
            () => new GraphQLAssertionException(
                ProtoStatusAssertion.DescribeFailure(
                    expected,
                    StatusCode,
                    negated,
                    ProtoHttpDiagnosticSanitizer.SanitizeBody(Content, ResolveStatusDiagnosticOptions(ProtoGraphQLBuilder.ProtocolName)))));
        return this;
    }

    /// <summary>Asserts the response carries no GraphQL errors.</summary>
    /// <remarks>Obsolete: use <c>response.Should.HaveNoErrors()</c>.</remarks>
    [Obsolete("Use response.Should.HaveNoErrors() instead.")]
    public GraphQLResponse ShouldHaveNoErrors() => AssertNoErrors(negated: false);

    /// <summary>Asserts the response carries at least one GraphQL error.</summary>
    /// <remarks>Obsolete: use <c>response.Should.HaveErrors()</c>.</remarks>
    [Obsolete("Use response.Should.HaveErrors() instead.")]
    public GraphQLResponse ShouldHaveErrors() => AssertHasErrors(negated: false);

    /// <summary>Asserts a GraphQL error carries <paramref name="code"/> in <c>extensions.code</c>.</summary>
    /// <remarks>Obsolete: use <c>response.Should.HaveError(code)</c>.</remarks>
    [Obsolete("Use response.Should.HaveError(code) instead.")]
    public GraphQLResponse ShouldHaveError(string code) => AssertHasError(code, negated: false);

    // A negated title states the opposite assertion rather than prefixing the positive one, so it
    // reads "Assert GraphQL has errors" instead of "not Assert no GraphQL errors"; the status
    // assertion expresses its polarity the same way, inside the sentence.
    internal GraphQLResponse AssertNoErrors(bool negated)
        => Assert(
            "assert.graphql.no_errors",
            negated ? "Assert GraphQL has errors" : "Assert no GraphQL errors",
            negated,
            new Dictionary<string, string?> { ["actual.error_count"] = Errors.Count.ToString() },
            () => !HasErrors,
            () => negated
                ? "Expected GraphQL errors, but the response contained none."
                : $"Expected no GraphQL errors, but received {Errors.Count}: {ErrorMessages()}");

    internal GraphQLResponse AssertHasErrors(bool negated)
        => Assert(
            "assert.graphql.has_errors",
            negated ? "Assert GraphQL has no errors" : "Assert GraphQL has errors",
            negated,
            new Dictionary<string, string?> { ["actual.error_count"] = Errors.Count.ToString() },
            () => HasErrors,
            () => negated
                ? $"Expected no GraphQL errors, but received {Errors.Count}: {ErrorMessages()}"
                : "Expected at least one GraphQL error, but the response contained none.");

    internal GraphQLResponse AssertHasError(string code, bool negated)
        => Assert(
            "assert.graphql.error_code",
            negated
                ? $"Assert GraphQL does not have an error with code '{code}'"
                : $"Assert GraphQL error · {code}",
            negated,
            new Dictionary<string, string?>
            {
                ["expected.error_code"] = code,
                ["actual.error_codes"] = ErrorCodes()
            },
            () => Errors.Any(error => string.Equals(error.Code, code, StringComparison.OrdinalIgnoreCase)),
            () => negated
                ? $"Expected no GraphQL error with code '{code}', but found: {ErrorCodes()}."
                : $"Expected a GraphQL error with code '{code}', but found: {ErrorCodes()}.");

    /// <summary>
    /// Matches the selected data against an expected shape through the same matcher REST, GraphQL,
    /// gRPC and messaging share.
    /// </summary>
    /// <remarks>Obsolete: use <c>response.Should.MatchShape(shape)</c>.</remarks>
    [Obsolete("Use response.Should.MatchShape(shape) instead.")]
    public GraphQLResponse ShouldMatchShape(object expectedShape, JsonSerializerOptions? options = null)
        => AssertResponseShape(expectedShape, options);

    /// <summary>
    /// The shape assertion behind <see cref="GraphQLShouldAssertions.MatchShape"/> and the obsolete
    /// <see cref="ShouldMatchShape"/> shim. A matcher failure is rethrown as a
    /// <see cref="GraphQLAssertionException"/> whose message starts with the operation identifier,
    /// with the matcher exception - and its mismatch list - as the inner exception.
    /// </summary>
    internal GraphQLResponse AssertResponseShape(object expectedShape, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(expectedShape);
        if (SelectedData is not { } selected || selected.ValueKind == JsonValueKind.Null)
        {
            // A data-less response (errors-only, or "data": null) has nothing to match against; the
            // shared assertion records the failure the same way a mismatch is recorded, with the
            // GraphQL-specific failure the docs promise.
            ProtoShapeAssertion.AssertMissing(
                new ProtoShapeAssertionContext(
                    Context,
                    ProtoGraphQLBuilder.Protocol.TraceSource,
                    "Assert GraphQL data shape",
                    ParentOperationId: RequestTraceId,
                    ExtraAttributes: new Dictionary<string, string?> { ["graphql.operation"] = Identifier }),
                expectedShape,
                () => new GraphQLAssertionException(DescribeMissingData()));
        }

        try
        {
            AssertShape(
                ProtoGraphQLBuilder.Protocol.TraceSource,
                "Assert GraphQL data shape",
                SelectedData?.GetRawText(),
                expectedShape,
                options,
                new Dictionary<string, string?> { ["graphql.operation"] = Identifier },
                matched => Context is not null && !string.IsNullOrEmpty(TargetName) && !string.IsNullOrEmpty(Identifier)
                    ? new ProtoObservation(
                        TargetName!,
                        ProtoGraphQLBuilder.ShapeObservationKind,
                        Identifier!,
                        new GraphQLShapeMatchData(Identifier!, matched))
                    : null);
        }
        catch (JsonShapeMismatchException exception)
        {
            throw new GraphQLAssertionException(PrefixIdentifier(exception.Message), exception);
        }
        catch (JsonDocumentAssertionException exception)
        {
            throw new GraphQLAssertionException(PrefixIdentifier(exception.Message), exception);
        }

        return this;
    }

    // The operation identifier the protocol reported the call under is the subject a mismatch or a
    // required read names; a response asserted without one keeps the message unchanged.
    private string PrefixIdentifier(string message)
        => string.IsNullOrEmpty(Identifier) ? message : $"{Identifier} — {message}";

    // An errors-only response has nothing to match, and the server's own message is what the author
    // needs to see: a rejected subscription or a failed operation must not read as "no data".
    private string DescribeMissingData()
        => HasErrors
            ? $"Expected GraphQL data, but the response did not contain data. Server errors: {ErrorMessages()}."
            : "Expected GraphQL data, but the response did not contain data.";

    private GraphQLAssertionException RequiredFailure<T>(string? jsonPath, string reason)
        => new(PrefixIdentifier($"{ProtoJsonRead.DescribeRequired<T>(jsonPath)} failed: {reason}."));

    /// <summary>
    /// Deserializes the selected data as <typeparamref name="T"/>, or returns <c>default</c> when the
    /// response has no data. A failure records a <c>graphql.response.deserialize</c> operation and
    /// rethrows.
    /// </summary>
    public T? ReadDataAs<T>(JsonSerializerOptions? options = null)
        => ReadDataAsCore<T>(jsonPath: null, required: false, options);

    /// <summary>
    /// Reads a single value from the selected data at <paramref name="jsonPath"/> and deserializes it
    /// as <typeparamref name="T"/>, for example <c>ReadDataAs&lt;int&gt;("$.order.total")</c>. The
    /// supported subset is <c>$</c>, dot members and <c>[n]</c> indices (see
    /// <see cref="JsonPathResolver"/>); a leading member without <c>$</c> is accepted. A response with
    /// no data returns <c>default</c>; a path that does not resolve throws
    /// <see cref="GraphQLAssertionException"/> naming the operation and the path.
    /// </summary>
    public T? ReadDataAs<T>(string jsonPath, JsonSerializerOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonPath);
        return ReadDataAsCore<T>(jsonPath, required: false, options);
    }

    /// <summary>
    /// Reads the selected data as <typeparamref name="T"/> and fails when there is nothing to return: a
    /// response without data, or data that is JSON <c>null</c>, throws
    /// <see cref="GraphQLAssertionException"/> naming the operation instead of returning <c>default</c>.
    /// Use it where the value is required; <see cref="ReadDataAs{T}(JsonSerializerOptions?)"/> stays
    /// nullable.
    /// </summary>
    public T ReadRequired<T>(JsonSerializerOptions? options = null)
    {
        EnsureDataIsNotNull<T>(jsonPath: null);
        return ReadDataAsCore<T>(jsonPath: null, required: true, options)!;
    }

    /// <summary>
    /// Reads the value at <paramref name="jsonPath"/> as <typeparamref name="T"/> and fails when the
    /// response has no data or the path holds JSON <c>null</c> — for every <typeparamref name="T"/>,
    /// including value types, because the null check runs before the deserializer. A path that does not
    /// resolve fails through <see cref="ReadDataAs{T}(string, JsonSerializerOptions?)"/> with the
    /// operation and the path.
    /// </summary>
    public T ReadRequired<T>(string jsonPath, JsonSerializerOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonPath);
        EnsureDataIsNotNull<T>(jsonPath);
        return ReadDataAsCore<T>(jsonPath, required: true, options)!;
    }

    // GraphQL's half of the shared read: its exception type and its graphql.response.deserialize
    // operation. The sink fails the operation for every failure the read detects, once. A response
    // built without an execution context (an untraced assertion) still reads, like REST's.
    private T? ReadDataAsCore<T>(string? jsonPath, bool required, JsonSerializerOptions? options)
    {
        var scope = Context?.Trace
            .Operation("graphql.response.deserialize", $"Deserialize GraphQL data · {typeof(T).Name}", ProtoGraphQLBuilder.Protocol.TraceSource)
            .With("target.type", typeof(T).FullName)
            .Parent(RequestTraceId);
        if (jsonPath is not null)
        {
            scope = scope?.With("graphql.path", jsonPath);
        }

        using var operation = scope?.Begin();
        var value = ProtoJsonRead.Read<T>(
            SelectedData?.GetRawText(),
            jsonPath,
            required,
            new ProtoJsonReadSemantics(
                RequiredFailure: message => new GraphQLAssertionException(PrefixIdentifier(message)),
                PathMissFailure: (message, inner) => new GraphQLAssertionException(PrefixIdentifier(message), inner),
                EmptyBodyReason: "the response did not contain data",
                NullBodyReason: "the response data was JSON null",
                TraceFailure: operation is null ? null : (exception, _) => operation.Fail(exception),
                NullRootReturnsDefault: true),
            options);
        operation?.Succeed();
        return value;
    }

    // A required read without data fails before the read: there is nothing to resolve, and the message
    // names the missing data rather than a path that could never have resolved.
    private void EnsureDataIsNotNull<T>(string? jsonPath)
    {
        if (SelectedData is not { } data || data.ValueKind == JsonValueKind.Null)
        {
            throw RequiredFailure<T>(jsonPath, "the response did not contain data");
        }
    }

    /// <summary>Disposes the parsed document and then the underlying HTTP response.</summary>
    public override void Dispose()
    {
        _document.Dispose();
        base.Dispose();
    }

    private GraphQLResponse Assert(
        string kind,
        string name,
        bool negated,
        IReadOnlyDictionary<string, string?> attributes,
        Func<bool> holds,
        Func<string> describeFailure)
    {
        // An untraced assertion (a response built without an execution context) still asserts; it just
        // records no operation. REST's status and shape assertions tolerate the same null context.
        var scope = Context?.Trace
            .Operation(kind, name, ProtoGraphQLBuilder.Protocol.TraceSource)
            .With(attributes);
        if (negated)
        {
            // The positive evidence is unchanged from the pre-facade assertions; the polarity is only
            // recorded when there is one.
            scope = scope?.With("assertion.negated", "true");
        }

        using var operation = scope?.Parent(RequestTraceId).Begin();
        try
        {
            if (!ProtoAssertion.IsSatisfied(holds(), negated))
            {
                throw new GraphQLAssertionException(describeFailure());
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

    private string ErrorMessages() => string.Join("; ", Errors.Select(error => error.Message));

    private string ErrorCodes() => string.Join(", ", Errors.Select(error => error.Code ?? "<none>"));

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
