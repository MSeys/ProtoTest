namespace ProtoTest.Rest;

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.Json;
using ProtoTest.Rest.Internal;

public sealed class RestRequestBuilder : ProtoHttpRequestBuilder<RestResponse, RestRequestBuilder>
{
    private readonly HashSet<string> _tracedHeaders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _requestAttributes = new(StringComparer.Ordinal);
    private Func<HttpContent>? _contentFactory;

    internal RestRequestBuilder(
        HttpClient httpClient,
        ProtoExecutionContext context,
        string targetName,
        string? clientEntityName = null)
        : base(httpClient, context, targetName, ProtoRestBuilder.Protocol, clientEntityName)
    {
    }

    protected override void OnAuthenticationConfigured(string source, Type? authenticatorType)
    {
        Configure(("auth.source", source));
        if (authenticatorType is not null)
        {
            Configure(("auth.type", authenticatorType.FullName));
        }
    }

    protected override void OnHeaderConfigured(string name, bool isNewHeader)
        => Configure(("http.request.header_count", Headers.Count.ToString()));

    public RestRequestBuilder Body(object payload, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        // Request payloads follow the shared web defaults (camelCase names), the same options
        // GraphQL variables use, so one DTO serializes the same through every protocol.
        var json = JsonSerializer.Serialize(payload, options ?? ProtoJsonDefaults.Web);
        return Body(json, "application/json");
    }

    public RestRequestBuilder Body(string rawContent, string mediaType = "text/plain")
    {
        ArgumentNullException.ThrowIfNull(rawContent);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        _contentFactory = () => new StringContent(rawContent, Encoding.UTF8, mediaType);
        Configure(
            ("http.request.body.media_type", mediaType),
            ("http.request.body.length", Encoding.UTF8.GetByteCount(rawContent).ToString()));
        return this;
    }

    public RestRequestBuilder Body(ReadOnlyMemory<byte> content, string mediaType = "application/octet-stream")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        var bytes = content.ToArray();
        Configure(
            ("http.request.body.media_type", mediaType),
            ("http.request.body.length", bytes.Length.ToString()));
        _contentFactory = () =>
        {
            var byteContent = new ByteArrayContent(bytes);
            byteContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
            return byteContent;
        };
        return this;
    }

    /// <summary>Supplies fresh request content for each send, allowing a builder to be reused safely.</summary>
    public RestRequestBuilder Body(Func<HttpContent> contentFactory)
    {
        _contentFactory = contentFactory ?? throw new ArgumentNullException(nameof(contentFactory));
        Configure(("http.request.body.source", "factory"));
        return this;
    }

    public Task<RestResponse> GetAsync(string routeTemplate, object? routeAndQueryParams = null, CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, routeTemplate, routeAndQueryParams, ct);

    public Task<RestResponse> PostAsync(string routeTemplate, object? routeAndQueryParams = null, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, routeTemplate, routeAndQueryParams, ct);

    public Task<RestResponse> PutAsync(string routeTemplate, object? routeAndQueryParams = null, CancellationToken ct = default)
        => SendAsync(HttpMethod.Put, routeTemplate, routeAndQueryParams, ct);

    public Task<RestResponse> PatchAsync(string routeTemplate, object? routeAndQueryParams = null, CancellationToken ct = default)
        => SendAsync(HttpMethod.Patch, routeTemplate, routeAndQueryParams, ct);

    public Task<RestResponse> DeleteAsync(string routeTemplate, object? routeAndQueryParams = null, CancellationToken ct = default)
        => SendAsync(HttpMethod.Delete, routeTemplate, routeAndQueryParams, ct);

    public Task<RestResponse> HeadAsync(string routeTemplate, object? routeAndQueryParams = null, CancellationToken ct = default)
        => SendAsync(HttpMethod.Head, routeTemplate, routeAndQueryParams, ct);

    public Task<RestResponse> OptionsAsync(string routeTemplate, object? routeAndQueryParams = null, CancellationToken ct = default)
        => SendAsync(HttpMethod.Options, routeTemplate, routeAndQueryParams, ct);

    public async Task<RestResponse> SendAsync(
        HttpMethod method,
        string routeTemplate,
        object? routeAndQueryParams = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(method);
        var attachmentOptions = Context.ResolveAttachmentOptions(ProtoRestBuilder.ProtocolName);
        using var traceOperation = Context.Trace
            .Operation("http.request", $"REST · {method.Method.ToUpperInvariant()} {routeTemplate}", ProtoRestBuilder.Protocol.TraceSource)
            .ForClient(typeof(HttpClient), TargetName, ClientEntityName)
            .With("http.request.method", method.Method.ToUpperInvariant())
            .With("http.route", routeTemplate)
            .With(_requestAttributes)
            .Begin();
        TraceConfiguredHeaders(traceOperation);
        var stopwatch = Stopwatch.StartNew();

        // One failure arm for every phase: stop the clock, fail the operation, record the failure
        // observation, and rethrow so the caller sees the original exception.
        async Task<TResult> RunAsync<TResult>(Func<Task<TResult>> phase, Uri? uri)
        {
            try
            {
                return await phase();
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                traceOperation.Fail(exception);
                TryRecordFailure(method, routeTemplate, uri, stopwatch.Elapsed, exception, ct, attachmentOptions);
                throw;
            }
        }

        var requestUri = await RunAsync(
            () => ResolveRequestUriAsync(routeTemplate, routeAndQueryParams, traceOperation, attachmentOptions, ct),
            uri: null);

        using var request = new HttpRequestMessage(method, requestUri);
        var attachmentPrefix = NextAttachmentPrefix(attachmentOptions);
        var attachmentDescription = $"{method.Method.ToUpperInvariant()} {routeTemplate}";
        await RunAsync(
            async () =>
            {
                await PrepareRequestAsync(request, traceOperation, attachmentOptions, attachmentPrefix, attachmentDescription, ct);
                return true;
            },
            request.RequestUri);

        return await RunAsync(
            () => SendAndObserveAsync(
                method,
                routeTemplate,
                request,
                traceOperation,
                stopwatch,
                attachmentOptions,
                attachmentPrefix,
                attachmentDescription,
                ct),
            request.RequestUri);
    }

    /// <summary>Builds the request URI and records it on the operation, sanitized.</summary>
    private async Task<Uri> ResolveRequestUriAsync(
        string routeTemplate,
        object? routeAndQueryParams,
        ProtoTraceOperation traceOperation,
        ProtoHttpAttachmentOptions? attachmentOptions,
        CancellationToken ct)
    {
        var requestUri = await RestUriBuilder.BuildRequestUriAsync(
            routeTemplate,
            routeAndQueryParams,
            Client.BaseAddress,
            BaseAddressResolver,
            Context,
            ct);
        traceOperation.SetAttribute(
            "http.request.url",
            ProtoHttpDiagnosticSanitizer.SanitizeUri(requestUri, attachmentOptions));
        return requestUri;
    }

    /// <summary>Attaches the body, applies the headers and authentication, and records the request facts.</summary>
    private async Task PrepareRequestAsync(
        HttpRequestMessage request,
        ProtoTraceOperation traceOperation,
        ProtoHttpAttachmentOptions? attachmentOptions,
        string? attachmentPrefix,
        string attachmentDescription,
        CancellationToken ct)
    {
        if (_contentFactory is not null)
        {
            request.Content = _contentFactory()
                ?? throw new InvalidOperationException("The REST request content factory returned null.");

            if (attachmentOptions?.CaptureRequestBodies == true)
            {
                var requestBody = await request.Content.ReadAsStringAsync(ct);
                var mediaType = request.Content.Headers.ContentType?.MediaType;
                Context.AddAttachment(
                    $"{attachmentPrefix}-request",
                    ProtoHttpDiagnosticSanitizer.SanitizeBody(requestBody, attachmentOptions),
                    mediaType ?? "text/plain",
                    attachmentDescription);
            }
        }

        ProtoHttpHeaders.Apply(request, Headers);

        await ApplyAuthenticationAsync(request, traceOperation, ct);

        var requestFacts = new List<ProtoTraceSectionItem>
        {
            new("url", ProtoHttpDiagnosticSanitizer.SanitizeUri(request.RequestUri, attachmentOptions)),
            new("auth", AuthenticatorFactory is null ? "none" : ResolvedAuthenticator?.GetType().Name ?? "applied")
        };
        if (_requestAttributes.TryGetValue("http.request.body.media_type", out var bodyMediaType) && bodyMediaType is not null)
        {
            requestFacts.Add(new(
                "body",
                bodyMediaType,
                _requestAttributes.GetValueOrDefault("http.request.body.length") is { } bodyLength ? $"{bodyLength} B" : null));
        }
        if (Headers.Count > 0)
        {
            requestFacts.Add(new("headers", Headers.Count.ToString()));
        }
        traceOperation.AddSection(new ProtoTraceSection("Request", ProtoTraceSectionKind.Fields, requestFacts));
    }

    /// <summary>Sends the request, records the response facts and attachments, and wraps the response.</summary>
    private async Task<RestResponse> SendAndObserveAsync(
        HttpMethod method,
        string routeTemplate,
        HttpRequestMessage request,
        ProtoTraceOperation traceOperation,
        Stopwatch stopwatch,
        ProtoHttpAttachmentOptions? attachmentOptions,
        string? attachmentPrefix,
        string attachmentDescription,
        CancellationToken ct)
    {
        HttpResponseMessage? responseMessage = null;
        try
        {
            var exchange = await ProtoHttpExchange.SendAsync(
                Client,
                request,
                ResolveResponseOptions().MaxResponseBodyBytes,
                ct);
            responseMessage = exchange.Response;
            var bodyBytes = exchange.BodyBytes;
            var bodyString = exchange.Body;
            stopwatch.Stop();
            var responseMediaType = responseMessage.Content.Headers.ContentType?.MediaType;
            var diagnosticBody = ProtoHttpDiagnosticSanitizer.SanitizeBody(
                bodyString,
                attachmentOptions);

            var responseFacts = new List<ProtoTraceSectionItem>
            {
                new(
                    "status",
                    ((int)responseMessage.StatusCode).ToString(),
                    Tone: responseMessage.IsSuccessStatusCode ? ProtoTraceSectionTone.Success : ProtoTraceSectionTone.Warning)
            };
            if (responseMediaType is not null)
            {
                responseFacts.Add(new("type", responseMediaType));
            }
            responseFacts.Add(new("length", $"{bodyBytes.Length} B"));
            traceOperation.AddSection(new ProtoTraceSection("Response", ProtoTraceSectionKind.Fields, responseFacts));
            if (!string.IsNullOrWhiteSpace(diagnosticBody))
            {
                traceOperation.AddSection(new ProtoTraceSection(
                    "Body",
                    ProtoTraceSectionKind.Code,
                    Content: ProtoTraceContent.Preview(diagnosticBody),
                    Language: "json"));
            }

            if (attachmentOptions?.CaptureResponses == true)
            {
                var attachmentName = $"{attachmentPrefix}-response";
                var attachmentMediaType = responseMediaType ?? "application/octet-stream";
                var attachmentDescriptionText =
                    $"{attachmentDescription} returned {(int)responseMessage.StatusCode} ({responseMessage.StatusCode})";
                if (IsTextMediaType(responseMediaType))
                {
                    Context.AddAttachment(
                        attachmentName,
                        diagnosticBody,
                        attachmentMediaType,
                        attachmentDescriptionText);
                }
                else
                {
                    Context.AddAttachment(
                        attachmentName,
                        bodyBytes,
                        attachmentMediaType,
                        attachmentDescriptionText);
                }
            }

            var headersDict = ProtoHttpDiagnosticSanitizer.SanitizeHeaders(
                responseMessage.Headers.Concat(responseMessage.Content.Headers),
                attachmentOptions);

            var routeIdentifier = $"{method.Method.ToUpperInvariant()} {routeTemplate}";

            Context.RecordObservation(new ProtoObservation(
                TargetName: TargetName,
                Kind: ProtoRestBuilder.Protocol.ResponseObservationKind,
                Identifier: routeIdentifier,
                Data: new RestResponseData(
                    Method: method.Method,
                    RouteTemplate: routeTemplate,
                    StatusCode: (int)responseMessage.StatusCode,
                    ResponseBody: diagnosticBody,
                    Headers: headersDict,
                    RequestUri: ProtoHttpDiagnosticSanitizer.SanitizeUri(request.RequestUri, attachmentOptions),
                    Duration: stopwatch.Elapsed
                )
            ));

            traceOperation
                .SetAttribute("http.response.status_code", ((int)responseMessage.StatusCode).ToString());
            traceOperation.Succeed();

            var response = new RestResponse(
                responseMessage,
                bodyString,
                stopwatch.Elapsed,
                new ProtoHttpResponseContext(
                    Execution: Context,
                    TargetName: TargetName,
                    Identifier: routeIdentifier,
                    AttachmentOptions: attachmentOptions,
                    AttachmentPrefix: attachmentPrefix,
                    RequestTraceId: traceOperation.Id),
                bodyBytes);
            responseMessage = null; // The response owns the message from here on.
            return response;
        }
        catch
        {
            responseMessage?.Dispose();
            throw;
        }
    }

    /// <summary>The per-call attachment prefix, allocating the next request number when capture is on.</summary>
    private string? NextAttachmentPrefix(ProtoHttpAttachmentOptions? attachmentOptions)
    {
        if (attachmentOptions is null)
        {
            return null;
        }

        var state = Context.TryResolve<ProtoHttpContextState>(ProtoRestBuilder.Protocol.Key);
        if (state is null)
        {
            state = new ProtoHttpContextState();
            Context.SetContext(ProtoRestBuilder.Protocol.Key, state);
        }

        return $"rest-{state.NextRequestNumber():00}";
    }

    private static bool IsTextMediaType(string? mediaType)
    {
        if (mediaType is null) return false;
        var normalized = mediaType.ToLowerInvariant();
        return normalized.StartsWith("text/", StringComparison.Ordinal)
            || normalized is "application/json" or "application/xml" or "application/yaml"
                or "application/x-yaml" or "application/javascript"
                or "application/x-www-form-urlencoded" or "application/graphql"
            || normalized.EndsWith("+json", StringComparison.Ordinal)
            || normalized.EndsWith("+xml", StringComparison.Ordinal)
            || normalized.EndsWith("+yaml", StringComparison.Ordinal);
    }

    private void TraceConfiguredHeaders(ProtoTraceOperation operation)
    {
        // One event per distinct header per builder, matching GraphQL: a reused builder must not
        // duplicate the configure events on every send.
        foreach (var name in Headers.Keys)
        {
            if (!_tracedHeaders.Add(name)) continue;
            Context.Trace.WriteEvent(
                "http.header.configure",
                $"Header · {name}",
                ProtoRestBuilder.Protocol.TraceSource,
                outcome: ProtoTraceOutcome.Succeeded,
                attributes: new Dictionary<string, string?>
                {
                    ["http.header.name"] = name,
                    ["http.header.value_recorded"] = "false"
                },
                parentId: operation.Id);
        }
    }

    private void Configure(params (string Key, string? Value)[] attributes)
    {
        foreach (var (key, value) in attributes)
        {
            _requestAttributes[key] = value;
        }
    }

    private void TryRecordFailure(
        HttpMethod method,
        string routeTemplate,
        Uri? requestUri,
        TimeSpan duration,
        Exception exception,
        CancellationToken cancellationToken,
        ProtoHttpAttachmentOptions? attachmentOptions)
        => ProtoObservationCapture.TryRecord(Context, () =>
        {
            var diagnostics = ProtoHttpFailureDiagnostics.From(requestUri, exception, cancellationToken, attachmentOptions);
            return new ProtoObservation(
                TargetName: TargetName,
                Kind: ProtoRestBuilder.FailureObservationKind,
                Identifier: $"{method.Method.ToUpperInvariant()} {routeTemplate}",
                Data: new RestFailureData(
                    method.Method,
                    routeTemplate,
                    diagnostics.RequestUri,
                    duration,
                    diagnostics.ExceptionType,
                    diagnostics.Message,
                    diagnostics.IsCanceled));
        });
}
