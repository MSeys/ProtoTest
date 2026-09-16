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
    private Func<HttpContent>? _contentFactory;
    private Func<ProtoExecutionContext, IProtoHttpAuthenticator>? _authenticatorFactory;
    private IProtoHttpAuthenticator? _resolvedAuthenticator;
    private Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>>? _baseAddressResolver;

    internal RestRequestBuilder(
        HttpClient httpClient,
        ProtoExecutionContext context,
        string targetName,
        IProtoHttpAuthenticator? defaultAuthenticator)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _targetName = targetName ?? throw new ArgumentNullException(nameof(targetName));
        _resolvedAuthenticator = defaultAuthenticator;
        _authenticatorFactory = defaultAuthenticator is null ? null : _ => defaultAuthenticator;
    }

    public RestRequestBuilder Auth(IProtoHttpAuthenticator authenticator)
    {
        _resolvedAuthenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        _authenticatorFactory = _ => authenticator;
        TraceConfiguration("auth.select", $"Authentication · {authenticator.GetType().Name}", new Dictionary<string, string?>()
        {
            ["auth.source"] = "request",
            ["auth.type"] = authenticator.GetType().FullName
        });
        return this;
    }

    public RestRequestBuilder Auth<TAuth>(params object[] constructorArgs) where TAuth : class, IProtoHttpAuthenticator
    {
        _resolvedAuthenticator = null;
        _authenticatorFactory = context => ProtoAuthenticatorFactory.Create<TAuth>(context, constructorArgs);
        TraceConfiguration("auth.select", $"Authentication · {typeof(TAuth).Name}", new Dictionary<string, string?>()
        {
            ["auth.source"] = "request",
            ["auth.type"] = typeof(TAuth).FullName
        });
        return this;
    }

    /// <summary>Disables inherited or class-level authentication for this request builder.</summary>
    public RestRequestBuilder WithoutAuth()
    {
        _resolvedAuthenticator = null;
        _authenticatorFactory = null;
        TraceConfiguration("auth.disable", "Authentication · Disabled", new Dictionary<string, string?> { ["auth.source"] = "request" });
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
        TraceConfiguration("http.header.configure", $"Header · {name}", new Dictionary<string, string?>()
        {
            ["http.header.name"] = name,
            ["http.header.value_recorded"] = "false"
        });
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
        TraceConfiguration("http.body.configure", $"Body · {mediaType}", new Dictionary<string, string?>()
        {
            ["http.request.body.media_type"] = mediaType,
            ["http.request.body.length"] = Encoding.UTF8.GetByteCount(rawContent).ToString()
        });
        return this;
    }

    public RestRequestBuilder Body(ReadOnlyMemory<byte> content, string mediaType = "application/octet-stream")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        var bytes = content.ToArray();
        TraceConfiguration("http.body.configure", $"Body · {mediaType}", new Dictionary<string, string?>()
        {
            ["http.request.body.media_type"] = mediaType,
            ["http.request.body.length"] = bytes.Length.ToString()
        });
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
        TraceConfiguration("http.body.configure", "Body · Content factory", new Dictionary<string, string?>()
        {
            ["http.request.body.source"] = "factory"
        });
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
        using var traceOperation = _context.Trace.StartOperation(
            "http.request",
            $"REST · {method.Method.ToUpperInvariant()} {routeTemplate}",
            "ProtoTest.Rest",
            attributes: new Dictionary<string, string?>
            {
                ["client.name"] = _targetName,
                ["http.request.method"] = method.Method.ToUpperInvariant(),
                ["http.route"] = routeTemplate
            });
        var stopwatch = Stopwatch.StartNew();
        Uri requestUri;
        using var resolveOperation = _context.Trace.StartOperation(
            "http.route.resolve",
            $"Resolve route · {routeTemplate}",
            "ProtoTest.Rest",
            attributes: new Dictionary<string, string?> { ["http.route"] = routeTemplate });
        try
        {
            requestUri = await RestUriBuilder.BuildRequestUriAsync(
                routeTemplate,
                routeAndQueryParams,
                _httpClient.BaseAddress,
                _baseAddressResolver,
                _context,
                ct);
            resolveOperation.SetAttribute("server.address", RestDiagnosticSanitizer.SanitizeUri(requestUri, attachmentOptions));
            resolveOperation.Succeed();
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            resolveOperation.Fail(exception);
            traceOperation.Fail(exception);
            TryRecordFailure(method, routeTemplate, null, stopwatch.Elapsed, exception, ct, attachmentOptions);
            throw;
        }

        using var request = new HttpRequestMessage(method, requestUri);
        var restState = _context.TryContext<RestContextState>();
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
                        RestDiagnosticSanitizer.SanitizeBody(requestBody, mediaType, attachmentOptions),
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
                "REST",
                "ProtoTest.Rest",
                ct);
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
            var diagnosticBody = RestDiagnosticSanitizer.SanitizeBody(
                bodyString,
                responseMediaType,
                attachmentOptions);

            if (attachmentOptions?.CaptureResponses == true)
            {
                _context.AddAttachment(
                    $"{attachmentPrefix}-response",
                    diagnosticBody,
                    responseMediaType ?? "text/plain",
                    $"{attachmentDescription} returned {(int)responseMessage.StatusCode} ({responseMessage.StatusCode})");
            }

            var headersDict = RestDiagnosticSanitizer.SanitizeHeaders(
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
                    RequestUri: RestDiagnosticSanitizer.SanitizeUri(request.RequestUri, attachmentOptions),
                    Duration: stopwatch.Elapsed
                )
            ));

            traceOperation
                .SetAttribute("http.response.status_code", ((int)responseMessage.StatusCode).ToString())
                .SetAttribute("server.address", RestDiagnosticSanitizer.SanitizeUri(request.RequestUri, attachmentOptions));
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

    private void TraceConfiguration(string kind, string name, IReadOnlyDictionary<string, string?> attributes)
        => _context.Trace.WriteEvent(
            kind,
            name,
            "ProtoTest.Rest",
            outcome: ProtoTraceOutcome.Succeeded,
            attributes: attributes);

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
                    RestDiagnosticSanitizer.SanitizeUri(requestUri, attachmentOptions),
                    duration,
                    exception.GetType().FullName ?? exception.GetType().Name,
                    RestDiagnosticSanitizer.SanitizeBody(exception.Message, "text/plain", attachmentOptions),
                    cancellationToken.IsCancellationRequested || exception is OperationCanceledException)));
        }
        catch
        {
            // A diagnostic failure must never hide the original request failure.
        }
    }
}
