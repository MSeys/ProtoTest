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
    {
        try
        {
            return string.IsNullOrWhiteSpace(Content)
                ? default
                : JsonSerializer.Deserialize<T>(Content, options ?? ProtoJsonDefaults.Reader);
        }
        catch (Exception exception)
        {
            TraceDeserializeFailure<T>(exception);
            throw;
        }
    }

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
        return string.IsNullOrWhiteSpace(Content)
            ? default
            : ReadAtPath<T>(jsonPath, options, required: false);
    }

    /// <summary>
    /// Reads the whole body as <typeparamref name="T"/> and fails when there is nothing to return: an
    /// empty body or JSON <c>null</c> throws <see cref="RestAssertionException"/> naming the request
    /// identifier instead of returning <c>default</c>. Use it where the body is required;
    /// <see cref="ReadAsJson{T}(JsonSerializerOptions?)"/> stays nullable.
    /// </summary>
    public T ReadRequired<T>(JsonSerializerOptions? options = null)
    {
        EnsureBodyIsNotNull<T>(jsonPath: null);
        var value = ReadAsJson<T>(options);
        return value is null
            ? throw TraceRequiredFailure<T>(jsonPath: null, "the response body was JSON null")
            : value;
    }

    /// <summary>
    /// Reads the value at <paramref name="jsonPath"/> as <typeparamref name="T"/> and fails when the
    /// body is empty or the path holds JSON <c>null</c> — for every <typeparamref name="T"/>, including
    /// value types, because the null check runs before the deserializer. A path that does not resolve
    /// fails with the same subject and path as
    /// <see cref="ReadAsJson{T}(string, JsonSerializerOptions?)"/>.
    /// </summary>
    public T ReadRequired<T>(string jsonPath, JsonSerializerOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonPath);
        EnsureBodyIsNotNull<T>(jsonPath);
        return ReadAtPath<T>(jsonPath, options, required: true)!;
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
        AssertStatus(
            ProtoRestBuilder.Protocol.TraceSource,
            expectedStatusCode,
            negated,
            // The failure message must not exceed either limit: the response section applies even
            // when the protocol never opted into attachment capture, and the attachment options
            // keep their own redaction rules when capture is on.
            () => new RestStatusAssertionException(
                expectedStatusCode,
                StatusCode,
                ProtoHttpDiagnosticSanitizer.SanitizeBody(Content, ResolveStatusDiagnosticOptions()),
                negated));
        return this;
    }

    private ProtoHttpAttachmentOptions ResolveStatusDiagnosticOptions()
    {
        // The resolved ProtoTest:Rest:Responses section bounds the failure body even when the protocol
        // never opted into attachment capture; attachment options, when present, keep their own
        // redaction rules and may tighten the limit further.
        var responseLimit = Context?.ResolveResponseOptions(ProtoRestBuilder.ProtocolName).MaxDiagnosticBodyLength
            ?? new ProtoHttpResponseOptions().MaxDiagnosticBodyLength;
        if (AttachmentOptions is null)
            return new ProtoHttpAttachmentOptions { MaxDiagnosticBodyLength = responseLimit };
        if (responseLimit >= AttachmentOptions.MaxDiagnosticBodyLength)
            return AttachmentOptions;

        return new ProtoHttpAttachmentOptions
        {
            RedactSensitiveData = AttachmentOptions.RedactSensitiveData,
            SensitiveJsonProperties = [.. AttachmentOptions.SensitiveJsonProperties],
            MaxDiagnosticBodyLength = responseLimit
        };
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
    /// identifier, with the matcher exception - and its mismatch list - as the inner exception.
    /// </summary>
    internal RestResponse AssertResponseShape(object expectedShape, JsonSerializerOptions? options = null)
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
                            StatusCode: (int)StatusCode))
                    : null);
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
        => string.IsNullOrEmpty(Identifier) ? message : $"{Identifier} — {message}";

    private RestAssertionException RequiredFailure<T>(string? jsonPath, string reason)
    {
        var read = string.IsNullOrWhiteSpace(jsonPath)
            ? $"ReadRequired<{typeof(T).Name}>"
            : $"ReadRequired<{typeof(T).Name}>('{jsonPath}')";
        return new RestAssertionException(PrefixIdentifier($"{read} failed: {reason}."));
    }

    // A required read resolves the element before deserializing, so JSON null - which would otherwise
    // reach a value-type T as the deserializer's JsonException - fails as the protocol assertion. Every
    // failure, a path miss included, records the deserialize event with the path.
    private T? ReadAtPath<T>(string jsonPath, JsonSerializerOptions? options, bool required)
    {
        try
        {
            using var document = JsonDocument.Parse(Content);
            var element = JsonPathResolver.Resolve(document.RootElement, jsonPath);
            if (required && element.ValueKind == JsonValueKind.Null)
            {
                throw RequiredFailure<T>(jsonPath, $"the value at '{jsonPath}' was JSON null");
            }

            var value = element.Deserialize<T>(options ?? ProtoJsonDefaults.Reader);
            if (required && value is null)
            {
                throw RequiredFailure<T>(jsonPath, $"the value at '{jsonPath}' was JSON null");
            }

            return value;
        }
        catch (JsonPathException exception)
        {
            var failure = new RestAssertionException(PrefixIdentifier(exception.Message), exception);
            TraceDeserializeFailure<T>(failure, jsonPath);
            throw failure;
        }
        catch (Exception exception)
        {
            TraceDeserializeFailure<T>(exception, jsonPath);
            throw;
        }
    }

    // The required reads check the body before deserializing for the same reason, and an empty or
    // JSON-null body records the same deserialize event as any other read failure.
    private void EnsureBodyIsNotNull<T>(string? jsonPath)
    {
        if (string.IsNullOrWhiteSpace(Content))
        {
            throw TraceRequiredFailure<T>(jsonPath, "the response body was empty");
        }

        try
        {
            using var document = JsonDocument.Parse(Content);
            if (document.RootElement.ValueKind == JsonValueKind.Null)
            {
                throw TraceRequiredFailure<T>(jsonPath, "the response body was JSON null");
            }
        }
        catch (JsonException exception)
        {
            TraceDeserializeFailure<T>(exception, jsonPath);
            throw;
        }
    }

    private RestAssertionException TraceRequiredFailure<T>(string? jsonPath, string reason)
    {
        var failure = RequiredFailure<T>(jsonPath, reason);
        TraceDeserializeFailure<T>(failure, jsonPath);
        return failure;
    }

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
