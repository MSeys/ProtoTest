namespace ProtoTest.Rest;

using System.Dynamic;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.Json;
using ProtoTest.Rest.Exceptions;
using ProtoTest.Rest.Internal;

public sealed class RestResponse : ProtoHttpResponse, IProtoBinaryContent
{
    private RestShouldAssertions? _should;
    private RestAssertions? _shouldNot;

    internal RestResponse(
        HttpResponseMessage rawResponse,
        string content,
        TimeSpan elapsedTime,
        ProtoHttpResponseContext? context = null,
        ReadOnlyMemory<byte>? contentBytes = null)
        : base(rawResponse, content, elapsedTime, context)
    {
        ContentBytes = contentBytes ?? System.Text.Encoding.UTF8.GetBytes(Content);
    }

    /// <summary>
    /// The positive assertions on this response, such as <c>Should.HaveHttpStatus(...)</c> and
    /// <c>Should.MatchShape(...)</c>.
    /// </summary>
    public RestShouldAssertions Should => _should ??= new RestShouldAssertions(this);

    /// <summary>
    /// Assertions that must not hold, such as <c>ShouldNot.HaveHttpStatus(...)</c>. Shape assertions
    /// stay on <see cref="Should"/> because a negated shape match is not meaningful.
    /// </summary>
    public RestAssertions ShouldNot => _shouldNot ??= new RestAssertions(this, negated: true);

    public bool IsSuccessStatusCode => RawResponse.IsSuccessStatusCode;
    public HttpResponseHeaders Headers => RawResponse.Headers;
    public HttpContentHeaders ContentHeaders => RawResponse.Content.Headers;
    public ReadOnlyMemory<byte> ContentBytes { get; }

    string? IProtoBinaryContent.MediaType => ContentHeaders.ContentType?.MediaType;

    ReadOnlyMemory<byte> IProtoBinaryContent.Content => ContentBytes;

    string? IProtoBinaryContent.FileName => RawResponse.Content.Headers.ContentDisposition is { } disposition
        ? (disposition.FileNameStar ?? disposition.FileName)?.Trim('"')
        : null;

    /// <summary>
    /// Deserializes the whole body as <typeparamref name="T"/>, or returns <c>default</c> for an empty
    /// body. A failure records an <c>http.response.deserialize</c> event and rethrows.
    /// </summary>
    public T? ReadAsJson<T>(JsonSerializerOptions? options = null)
        => ProtoJsonRead.Read<T>(Content, jsonPath: null, required: false, SemanticsFor<T>(), options);

    /// <summary>
    /// Reads a single value from the body at <paramref name="jsonPath"/> and deserializes it as
    /// <typeparamref name="T"/>, for example <c>ReadAsJson&lt;int&gt;("$.id")</c>. The supported subset
    /// is <c>$</c>, dot members and <c>[n]</c> indices (see <see cref="JsonPathResolver"/>); a leading
    /// member without <c>$</c> is accepted. An empty body returns <c>default</c>; a path that does not
    /// resolve throws <see cref="Exceptions.RestAssertionException"/> naming the request identifier and
    /// the path. Values of the wrong type throw <see cref="JsonException"/>, and the failure is traced
    /// like any deserialization error, with the path.
    /// </summary>
    public T? ReadAsJson<T>(string jsonPath, JsonSerializerOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonPath);
        return ProtoJsonRead.Read<T>(Content, jsonPath, required: false, SemanticsFor<T>(), options);
    }

    /// <summary>
    /// Reads the whole body as <typeparamref name="T"/> and fails when there is nothing to return: an
    /// empty body or JSON <c>null</c> throws <see cref="RestAssertionException"/> naming the request
    /// identifier instead of returning <c>default</c>. Use it where the body is required;
    /// <see cref="ReadAsJson{T}(JsonSerializerOptions?)"/> stays nullable.
    /// </summary>
    public T ReadRequired<T>(JsonSerializerOptions? options = null)
        => ProtoJsonRead.Read<T>(Content, jsonPath: null, required: true, SemanticsFor<T>(), options)!;

    /// <summary>
    /// Reads the value at <paramref name="jsonPath"/> as <typeparamref name="T"/> and fails when the
    /// body is empty or the path holds JSON <c>null</c>, for every <typeparamref name="T"/> including
    /// value types, because the null check runs before the deserializer. A path that does not resolve
    /// fails with the same subject and path as
    /// <see cref="ReadAsJson{T}(string, JsonSerializerOptions?)"/>.
    /// </summary>
    public T ReadRequired<T>(string jsonPath, JsonSerializerOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonPath);
        return ProtoJsonRead.Read<T>(Content, jsonPath, required: true, SemanticsFor<T>(), options)!;
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
        var mediaType = RawResponse.Content?.Headers.ContentType?.MediaType;
        AssertStatus(
            ProtoRestBuilder.Protocol.TraceSource,
            expectedStatusCode,
            negated,
            // The failure message must not exceed either limit: the shared base helper keeps the
            // protocol's response section bound and the attachment redaction rules. Binary bodies
            // stay out of the message; an empty body reads as a bare status mismatch.
            () => new RestStatusAssertionException(
                expectedStatusCode,
                StatusCode,
                ProtoMediaTypes.IsTextMediaType(mediaType)
                    ? ProtoHttpDiagnosticSanitizer.SanitizeBody(Content, ResolveStatusDiagnosticOptions(ProtoRestBuilder.ProtocolName))
                    : string.Empty,
                negated));
        return this;
    }

    /// <summary>
    /// Matches the response body against an expected shape through the same matcher REST, GraphQL,
    /// gRPC and messaging share.
    /// </summary>
    /// <remarks>Obsolete: use <c>response.Should.MatchShape(shape)</c>.</remarks>
    [Obsolete("Use response.Should.MatchShape(shape) instead.")]
    public RestResponse ShouldMatchShape(object expectedShape, JsonSerializerOptions? options = null)
        => AssertResponseShape(expectedShape, options);

    /// <summary>
    /// The shape assertion behind <see cref="RestShouldAssertions.MatchShape"/> and the obsolete
    /// <see cref="ShouldMatchShape"/> shim. A matcher failure is rethrown as a
    /// <see cref="Exceptions.RestAssertionException"/> whose message starts with the request
    /// identifier, with the matcher exception - and its mismatch list - as the inner exception. In
    /// exact mode a field present in the body that the shape does not mention is a mismatch.
    /// </summary>
    internal RestResponse AssertResponseShape(object expectedShape, JsonSerializerOptions? options = null, bool exact = false)
    {
        ArgumentNullException.ThrowIfNull(expectedShape);
        try
        {
            AssertShape(
                ProtoRestBuilder.Protocol.TraceSource,
                "Assert response shape",
                Content,
                expectedShape,
                options,
                new Dictionary<string, string?>
                {
                    ["actual.media_type"] = RawResponse.Content.Headers.ContentType?.MediaType,
                    ["request.identifier"] = Identifier
                },
                matched => Context is not null && !string.IsNullOrEmpty(TargetName) && !string.IsNullOrEmpty(Identifier)
                    ? new ProtoObservation(
                        TargetName: TargetName,
                        Kind: ProtoRestBuilder.ShapeObservationKind,
                        Identifier: Identifier,
                        Data: new RestShapeMatchData(
                            RequestIdentifier: Identifier,
                            MatchedProperties: matched,
                            TargetType: expectedShape.GetType(),
                            StatusCode: (int)StatusCode)
                        {
                            Method = RequestMethod,
                            RouteTemplate = RouteTemplate
                        })
                    : null,
                exact);
        }
        catch (JsonShapeMismatchException exception)
        {
            throw new RestAssertionException(PrefixIdentifier(exception.Message), exception);
        }
        catch (JsonDocumentAssertionException exception)
        {
            throw new RestAssertionException(PrefixIdentifier(exception.Message), exception);
        }

        return this;
    }

    // The identifier the protocol reported the call under is the subject a mismatch or a required read
    // names; a response asserted without one (an untraced assertion) keeps the message unchanged.
    private string PrefixIdentifier(string message)
        => string.IsNullOrEmpty(Identifier) ? message : $"{Identifier} - {message}";

    /// <summary>Asserts the response body's media type, without its parameters.</summary>
    internal RestResponse AssertContentType(string mediaType, bool negated)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        var actual = ContentHeaders.ContentType?.MediaType;
        var expected = $"'{mediaType}'";
        var holds = actual is not null && string.Equals(actual, mediaType, StringComparison.OrdinalIgnoreCase);
        return AssertHttpFact(
            "assert.http.content_type",
            $"Assert content type · {ProtoAssertion.Describe(expected, negated)}",
            new Dictionary<string, string?>
            {
                ["expected.content_type"] = mediaType,
                ["actual.content_type"] = actual
            },
            ResultItem("content type", actual, expected, holds, negated),
            holds,
            negated,
            () => $"Expected content type {ProtoAssertion.Describe(expected, negated)}, but received {DescribeValue(actual)}.");
    }

    /// <summary>Asserts the response carries a header, or that a header carries a value.</summary>
    internal RestResponse AssertHeader(string name, string? expectedValue, bool negated)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var values = ReadHeaderValues(name);
        var sanitized = SanitizeHeaderValues(name, values);
        var holds = expectedValue is null
            ? values.Count > 0
            : values.Any(value => string.Equals(value, expectedValue, StringComparison.Ordinal));
        var expectation = expectedValue is null
            ? "to be present"
            : $"to have value '{expectedValue}'";
        return AssertHttpFact(
            "assert.http.header",
            $"Assert header · {name}",
            new Dictionary<string, string?>
            {
                ["http.header.name"] = name,
                ["expected.header.value"] = expectedValue,
                ["actual.header.values"] = sanitized
            },
            ResultItem($"header '{name}'", sanitized, expectation, holds, negated),
            holds,
            negated,
            () => expectedValue is null
                ? $"Expected header '{name}' {ProtoAssertion.Describe(expectation, negated)}, but the response carried {(values.Count > 0 ? "it" : "none")}."
                : values.Count == 0
                    ? $"Expected header '{name}' {ProtoAssertion.Describe(expectation, negated)}, but the header was not present."
                    : $"Expected header '{name}' {ProtoAssertion.Describe(expectation, negated)}, but it was {sanitized}.");
    }

    /// <summary>Asserts the response sets a cookie, or that a cookie carries a value.</summary>
    internal RestResponse AssertCookie(string name, string? expectedValue, bool negated)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var cookies = ReadSetCookies();
        var matches = cookies.Where(cookie => string.Equals(cookie.Name, name, StringComparison.Ordinal)).ToArray();
        var holds = expectedValue is null
            ? matches.Length > 0
            : matches.Any(cookie => string.Equals(cookie.Value, expectedValue, StringComparison.Ordinal));
        var actual = matches.Length > 0 ? SanitizeHeaderValues("Set-Cookie", [matches[0].Raw]) : null;
        var expectation = expectedValue is null
            ? "to be set"
            : $"to have value '{expectedValue}'";
        return AssertHttpFact(
            "assert.http.cookie",
            $"Assert cookie · {name}",
            new Dictionary<string, string?>
            {
                ["http.cookie.name"] = name,
                ["expected.cookie.value"] = expectedValue,
                ["actual.cookie.value"] = actual
            },
            ResultItem($"cookie '{name}'", actual, expectation, holds, negated),
            holds,
            negated,
            () => expectedValue is null
                ? $"Expected cookie '{name}' {ProtoAssertion.Describe(expectation, negated)}, but the response set {(matches.Length > 0 ? "it" : "none")}."
                : matches.Length == 0
                    ? $"Expected cookie '{name}' {ProtoAssertion.Describe(expectation, negated)}, but the cookie was not set."
                    : $"Expected cookie '{name}' {ProtoAssertion.Describe(expectation, negated)}, but it was {actual}.");
    }

    /// <summary>Asserts the response's <c>Location</c> header, as it arrived.</summary>
    internal RestResponse AssertRedirectLocation(string location, bool negated)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        var actual = RawResponse.Headers.Location?.OriginalString;
        var expected = $"'{location}'";
        var holds = actual is not null && string.Equals(actual, location, StringComparison.Ordinal);
        return AssertHttpFact(
            "assert.http.redirect_location",
            $"Assert redirect location · {ProtoAssertion.Describe(expected, negated)}",
            new Dictionary<string, string?>
            {
                ["expected.location"] = location,
                ["actual.location"] = actual
            },
            ResultItem("redirect location", actual, expected, holds, negated),
            holds,
            negated,
            () => $"Expected redirect location {ProtoAssertion.Describe(expected, negated)}, but received {DescribeValue(actual)}.");
    }

    // The Checks item every HTTP fact assertion records: the actual value, and the expectation when it
    // did not hold. Presence-only assertions pass no expected value, so they read as "present".
    private static ProtoTraceSection ResultItem(
        string subject,
        string? actual,
        string? expected,
        bool holds,
        bool negated)
    {
        var satisfied = ProtoAssertion.IsSatisfied(holds, negated);
        return new ProtoTraceSection(
            "Result",
            ProtoTraceSectionKind.Checks,
            [
                new(
                    subject,
                    actual ?? "none",
                    satisfied ? null : $"expected {ProtoAssertion.Describe(expected ?? "present", negated)}",
                    satisfied ? ProtoTraceSectionTone.Success : ProtoTraceSectionTone.Error)
            ]);
    }

    // One assertion shape for the header-family facts: record the operation with its attributes and
    // Checks section, fail it, and rethrow the REST assertion exception naming the request.
    private RestResponse AssertHttpFact(
        string kind,
        string title,
        IReadOnlyDictionary<string, string?> attributes,
        ProtoTraceSection result,
        bool holds,
        bool negated,
        Func<string> describeFailure)
    {
        var scope = Context?.Trace
            .Operation(kind, title, ProtoRestBuilder.Protocol.TraceSource)
            .With(attributes)
            .With("assertion.negated", negated ? "true" : null)
            .With("request.identifier", Identifier)
            .Parent(RequestTraceId);
        using var operation = scope?.Begin();
        operation?.AddSection(result);
        try
        {
            if (!ProtoAssertion.IsSatisfied(holds, negated))
            {
                throw new RestAssertionException(PrefixIdentifier(describeFailure()));
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

    private static string DescribeValue(string? value) => value is null ? "none" : $"'{value}'";

    // Response headers and content headers are one namespace for the assertions, as they are for
    // header application: a custom content type is reachable through the content headers.
    private IReadOnlyList<string> ReadHeaderValues(string name)
    {
        foreach (var headers in new HttpHeaders[] { RawResponse.Headers, RawResponse.Content.Headers })
        {
            if (headers.TryGetValues(name, out var values)) return [.. values];
        }

        return [];
    }

    // The Set-Cookie pair before the first attribute; the raw header is kept for the sanitized display.
    private IReadOnlyList<(string Name, string Value, string Raw)> ReadSetCookies()
    {
        if (!RawResponse.Headers.TryGetValues("Set-Cookie", out var headers)) return [];
        var cookies = new List<(string, string, string)>();
        foreach (var header in headers)
        {
            var pair = header.Split(';', 2)[0];
            var separator = pair.IndexOf('=');
            if (separator <= 0) continue;
            cookies.Add((pair[..separator].Trim(), pair[(separator + 1)..].Trim(), header));
        }

        return cookies;
    }

    // Header and cookie values follow the shared redaction rules before they reach a message or the
    // trace, so an assertion failure never prints a token the diagnostics would hide.
    private string? SanitizeHeaderValues(string name, IReadOnlyList<string> values)
        => values.Count == 0
            ? null
            : ProtoHttpDiagnosticSanitizer.SanitizeHeaders(
                [new KeyValuePair<string, IEnumerable<string>>(name, values)],
                AttachmentOptions).Values.Single();

    // REST's half of the shared read: its exception type and its http.response.deserialize vocabulary.
    // Every failure the read detects is recorded once through TraceDeserializeFailure.
    private ProtoJsonReadSemantics SemanticsFor<T>() => new(
        RequiredFailure: message => new RestAssertionException(PrefixIdentifier(message)),
        PathMissFailure: (message, inner) => new RestAssertionException(PrefixIdentifier(message), inner),
        EmptyBodyReason: "the response body was empty",
        NullBodyReason: "the response body was JSON null",
        TraceFailure: (exception, jsonPath) => TraceDeserializeFailure<T>(exception, jsonPath));

    private void TraceDeserializeFailure<T>(Exception exception, string? jsonPath = null)
    {
        var attributes = new Dictionary<string, string?>
        {
            ["target.type"] = typeof(T).FullName,
            ["content.length"] = ContentBytes.Length.ToString()
        };
        if (!string.IsNullOrWhiteSpace(jsonPath))
        {
            attributes["json.path"] = jsonPath;
        }

        Context?.Trace.WriteEvent(
            "http.response.deserialize",
            $"Deserialize response · {typeof(T).Name}",
            ProtoRestBuilder.Protocol.TraceSource,
            outcome: ProtoTraceOutcome.Failed,
            attributes: attributes,
            exception: exception,
            parentId: RequestTraceId);
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
