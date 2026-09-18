namespace ProtoTest.Rest;

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.Rest.Internal;

public sealed class RestRequestBuilder
{
    private readonly HttpClient _httpClient;
    private readonly ProtoExecutionContext _context;
    private readonly string _targetName;
    private readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _requestAttributes = new(StringComparer.Ordinal);
    private Func<HttpContent>? _contentFactory;
    private Func<ProtoExecutionContext, IProtoHttpAuthenticator>? _authenticatorFactory;
    private IProtoHttpAuthenticator? _resolvedAuthenticator;
    private Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>>? _baseAddressResolver;

    internal RestRequestBuilder(
        HttpClient httpClient,
        ProtoExecutionContext context,
        string targetName)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _targetName = targetName ?? throw new ArgumentNullException(nameof(targetName));
    }

    public RestRequestBuilder Auth(IProtoHttpAuthenticator authenticator)
    {
        _resolvedAuthenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        _authenticatorFactory = _ => authenticator;
        Configure(("auth.source", "request"), ("auth.type", authenticator.GetType().FullName));
        return this;
    }

    public RestRequestBuilder Auth<TAuth>(params object[] constructorArgs) where TAuth : class, IProtoHttpAuthenticator
    {
        _resolvedAuthenticator = null;
        _authenticatorFactory = context => ProtoAuthenticatorFactory.Create<TAuth>(context, constructorArgs);
        Configure(("auth.source", "request"), ("auth.type", typeof(TAuth).FullName));
        return this;
    }

    /// <summary>Disables inherited or class-level authentication for this request builder.</summary>
    public RestRequestBuilder WithoutAuth()
    {
        _resolvedAuthenticator = null;
        _authenticatorFactory = null;
        Configure(("auth.source", "request"), ("auth.outcome", "disabled"));
        return this;
    }

    internal RestRequestBuilder UseAuthenticatorFactory(
        Func<ProtoExecutionContext, IProtoHttpAuthenticator>? authenticatorFactory)
    {
        _resolvedAuthenticator = null;
        _authenticatorFactory = authenticatorFactory;
        return this;
    }

    internal RestRequestBuilder UseBaseAddressResolver(
        Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>>? baseAddressResolver)
    {
        _baseAddressResolver = baseAddressResolver;
        return this;
    }

    public RestRequestBuilder Header(string name, string value)
    {
        _headers[name] = value;
        Configure(("http.request.header_count", _headers.Count.ToString()));
        return this;
    }

    public RestRequestBuilder Body(object payload, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var json = JsonSerializer.Serialize(payload, options);
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
        var attachmentOptions = _context.TryService<RestAttachmentOptions>();
        using var traceOperation = _context.Trace
            .Operation("http.request", $"REST · {method.Method.ToUpperInvariant()} {routeTemplate}", "ProtoTest.Rest")
            .For(ProtoTraceEntityKinds.Client, $"client:{typeof(HttpClient).FullName}:{_targetName}")
            .With("client.name", _targetName)
            .With("http.request.method", method.Method.ToUpperInvariant())
            .With("http.route", routeTemplate)
            .With(_requestAttributes)
            .Begin();
        var stopwatch = Stopwatch.StartNew();
        Uri requestUri;
        try
        {
            requestUri = await RestUriBuilder.BuildRequestUriAsync(
                routeTemplate,
                routeAndQueryParams,
                _httpClient.BaseAddress,
                _baseAddressResolver,
                _context,
                ct);
            traceOperation.SetAttribute(
                "http.request.url",
                ProtoHttpDiagnosticSanitizer.SanitizeUri(requestUri, attachmentOptions));
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            traceOperation.Fail(exception);
            TryRecordFailure(method, routeTemplate, null, stopwatch.Elapsed, exception, ct, attachmentOptions);
            throw;
        }

        using var request = new HttpRequestMessage(method, requestUri);
        var restState = _context.TryResolve<RestContextState>();
        if (restState is null)
        {
            restState = new RestContextState();
            _context.SetContext(restState);
        }
        var attachmentNumber = attachmentOptions is null
            ? (int?)null
            : restState.NextRequestNumber();
        var attachmentPrefix = attachmentNumber is null ? null : $"rest-{attachmentNumber:00}";
        var attachmentDescription = $"{method.Method.ToUpperInvariant()} {routeTemplate}";

        try
        {
            if (_contentFactory is not null)
            {
                request.Content = _contentFactory()
                    ?? throw new InvalidOperationException("The REST request content factory returned null.");

                if (attachmentOptions?.CaptureRequestBodies == true)
                {
                    var requestBody = await request.Content.ReadAsStringAsync(ct);
                    var mediaType = request.Content.Headers.ContentType?.MediaType;
                    _context.AddAttachment(
                        $"{attachmentPrefix}-request",
                        ProtoHttpDiagnosticSanitizer.SanitizeBody(requestBody, attachmentOptions),
                        mediaType ?? "text/plain",
                        attachmentDescription);
                }
            }

            foreach (var (key, value) in _headers)
            {
                if (!request.Headers.TryAddWithoutValidation(key, value)
                    && (request.Content is null || !request.Content.Headers.TryAddWithoutValidation(key, value)))
                {
                    throw new InvalidOperationException($"Header '{key}' could not be added to the REST request.");
                }
            }

            _resolvedAuthenticator = await ProtoHttpAuthenticationApplier.ApplyAsync(
                _authenticatorFactory,
                _resolvedAuthenticator,
                request,
                _context,
                _targetName,
                traceOperation,
                ct);

            var requestFacts = new List<ProtoTraceSectionItem>
            {
                new("url", ProtoHttpDiagnosticSanitizer.SanitizeUri(request.RequestUri, attachmentOptions)),
                new("auth", _authenticatorFactory is null ? "none" : _resolvedAuthenticator?.GetType().Name ?? "applied")
            };
            if (_requestAttributes.TryGetValue("http.request.body.media_type", out var bodyMediaType) && bodyMediaType is not null)
            {
                requestFacts.Add(new(
                    "body",
                    bodyMediaType,
                    _requestAttributes.GetValueOrDefault("http.request.body.length") is { } bodyLength ? $"{bodyLength} B" : null));
            }
            if (_headers.Count > 0)
            {
                requestFacts.Add(new("headers", _headers.Count.ToString()));
            }
            traceOperation.AddSection(new ProtoTraceSection("Request", ProtoTraceSectionKind.Fields, requestFacts));
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            traceOperation.Fail(exception);
            TryRecordFailure(
                method,
                routeTemplate,
                request.RequestUri,
                stopwatch.Elapsed,
                exception,
                ct,
                attachmentOptions);
            throw;
        }

        HttpResponseMessage? responseMessage = null;
        try
        {
            responseMessage = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct);
            var responseOptions = _context.TryService<RestResponseOptions>() ?? new RestResponseOptions();
            var bodyBytes = await ProtoHttpResponseBuffer.BufferAsync(
                responseMessage,
                responseOptions.MaxResponseBodyBytes,
                ct);
            var bodyString = await responseMessage.Content.ReadAsStringAsync(ct);
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
                    Content: Preview(diagnosticBody),
                    Language: "json"));
            }

            if (attachmentOptions?.CaptureResponses == true)
            {
                _context.AddAttachment(
                    $"{attachmentPrefix}-response",
                    diagnosticBody,
                    responseMediaType ?? "text/plain",
                    $"{attachmentDescription} returned {(int)responseMessage.StatusCode} ({responseMessage.StatusCode})");
            }

            var headersDict = ProtoHttpDiagnosticSanitizer.SanitizeHeaders(
                responseMessage.Headers.Concat(responseMessage.Content.Headers),
                attachmentOptions);

            var routeIdentifier = $"{method.Method.ToUpperInvariant()} {routeTemplate}";

            _context.RecordObservation(new ProtoObservation(
                TargetName: _targetName,
                Kind: "http.response",
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

            return new RestResponse(
                responseMessage,
                bodyString,
                stopwatch.Elapsed,
                _context,
                _targetName,
                routeIdentifier,
                attachmentOptions,
                attachmentPrefix,
                bodyBytes,
                traceOperation.Id
            );
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            traceOperation.Fail(exception);
            responseMessage?.Dispose();
            TryRecordFailure(
                method,
                routeTemplate,
                request.RequestUri,
                stopwatch.Elapsed,
                exception,
                ct,
                attachmentOptions);
            throw;
        }
    }

    private void Configure(params (string Key, string? Value)[] attributes)
    {
        foreach (var (key, value) in attributes)
        {
            _requestAttributes[key] = value;
        }
    }

    /// <summary>Pretty-prints a JSON body for the trace, capped so the artifact stays a trace and not a dump.</summary>
    private static string Preview(string body)
    {
        const int limit = 8000;
        try
        {
            using var document = JsonDocument.Parse(body);
            var pretty = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
            return pretty.Length > limit ? $"{pretty[..limit]}\n…" : pretty;
        }
        catch (JsonException)
        {
            return body.Length > limit ? $"{body[..limit]}…" : body;
        }
    }

    private void TryRecordFailure(
        HttpMethod method,
        string routeTemplate,
        Uri? requestUri,
        TimeSpan duration,
        Exception exception,
        CancellationToken cancellationToken,
        RestAttachmentOptions? attachmentOptions)
    {
        try
        {
            _context.RecordObservation(new ProtoObservation(
                TargetName: _targetName,
                Kind: "http.failure",
                Identifier: $"{method.Method.ToUpperInvariant()} {routeTemplate}",
                Data: new RestFailureData(
                    method.Method,
                    routeTemplate,
                    ProtoHttpDiagnosticSanitizer.SanitizeUri(requestUri, attachmentOptions),
                    duration,
                    exception.GetType().FullName ?? exception.GetType().Name,
                    ProtoHttpDiagnosticSanitizer.SanitizeBody(exception.Message, attachmentOptions),
                    cancellationToken.IsCancellationRequested || exception is OperationCanceledException)));
        }
        catch
        {
            // A diagnostic failure must never hide the original request failure.
        }
    }
}
