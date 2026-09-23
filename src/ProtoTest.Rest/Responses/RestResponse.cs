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
    private RestAssertions? _should;
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
    public ReadOnlyMemory<byte> ContentBytes { get; }

    string? IProtoBinaryContent.MediaType => ContentHeaders.ContentType?.MediaType;

    ReadOnlyMemory<byte> IProtoBinaryContent.Content => ContentBytes;

    string? IProtoBinaryContent.FileName => RawResponse.Content.Headers.ContentDisposition is { } disposition
        ? (disposition.FileNameStar ?? disposition.FileName)?.Trim('"')
        : null;

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
            Context?.Trace.WriteEvent(
                "http.response.deserialize",
                $"Deserialize response · {typeof(T).Name}",
                ProtoRestBuilder.Protocol.TraceSource,
                outcome: ProtoTraceOutcome.Failed,
                attributes: new Dictionary<string, string?>
                {
                    ["target.type"] = typeof(T).FullName,
                    ["content.length"] = ContentBytes.Length.ToString()
                },
                exception: exception,
                parentId: RequestTraceId);
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

    public RestResponse ShouldMatchShape(object expectedShape, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(expectedShape);
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
                    Kind: "http.contract.shape",
                    Identifier: Identifier,
                    Data: new RestShapeMatchData(
                        RequestIdentifier: Identifier,
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
