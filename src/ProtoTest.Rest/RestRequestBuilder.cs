namespace ProtoTest.Rest;

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Rest.Internal;

public sealed class RestRequestBuilder
{
    private readonly HttpClient _httpClient;
    private readonly ProtoExecutionContext _context;
    private readonly string _targetName;
    private readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);
    private Func<HttpContent>? _contentFactory;
    private Func<ProtoExecutionContext, IRestAuthenticator>? _authenticatorFactory;
    private IRestAuthenticator? _resolvedAuthenticator;
    private Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>>? _baseAddressResolver;

    internal RestRequestBuilder(
        HttpClient httpClient,
        ProtoExecutionContext context,
        string targetName,
        IRestAuthenticator? defaultAuthenticator)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _targetName = targetName ?? throw new ArgumentNullException(nameof(targetName));
        _resolvedAuthenticator = defaultAuthenticator;
        _authenticatorFactory = defaultAuthenticator is null ? null : _ => defaultAuthenticator;
    }

    public RestRequestBuilder Auth(IRestAuthenticator authenticator)
    {
        _resolvedAuthenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        _authenticatorFactory = _ => authenticator;
        return this;
    }

    public RestRequestBuilder Auth<TAuth>(params object[] constructorArgs) where TAuth : IRestAuthenticator
    {
        _resolvedAuthenticator = null;
        _authenticatorFactory = context => RestAuthenticatorFactory.Create<TAuth>(context, constructorArgs);
        return this;
    }

    /// <summary>Disables inherited or class-level authentication for this request builder.</summary>
    public RestRequestBuilder WithoutAuth()
    {
        _resolvedAuthenticator = null;
        _authenticatorFactory = null;
        return this;
    }

    internal RestRequestBuilder UseAuthenticatorFactory(
        Func<ProtoExecutionContext, IRestAuthenticator>? authenticatorFactory)
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
        return this;
    }

    public RestRequestBuilder Body(ReadOnlyMemory<byte> content, string mediaType = "application/octet-stream")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        var bytes = content.ToArray();
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
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
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

            if (_authenticatorFactory is not null)
            {
                _resolvedAuthenticator ??= _authenticatorFactory(_context)
                    ?? throw new InvalidOperationException("The REST authenticator factory returned null.");
                await _resolvedAuthenticator.AuthenticateAsync(
                    new RestAuthenticationContext(request, _context, _targetName),
                    ct);
            }
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
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
            var bodyBytes = await BufferResponseContentAsync(
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

            return new RestResponse(
                responseMessage,
                bodyString,
                stopwatch.Elapsed,
                _context,
                _targetName,
                routeIdentifier,
                attachmentOptions,
                attachmentPrefix,
                bodyBytes
            );
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
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

    private static async Task<byte[]> BufferResponseContentAsync(
        HttpResponseMessage response,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (maximumBytes < 0)
        {
            throw new InvalidOperationException("MaxResponseBodyBytes cannot be negative.");
        }

        var originalContent = response.Content;
        if (originalContent.Headers.ContentLength is > 0
            && originalContent.Headers.ContentLength > maximumBytes)
        {
            throw new Exceptions.RestResponseTooLargeException(
                maximumBytes,
                originalContent.Headers.ContentLength.Value);
        }

        await using var source = await originalContent.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream(Math.Min(maximumBytes, 64 * 1024));
        var buffer = new byte[16 * 1024];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > maximumBytes)
            {
                throw new Exceptions.RestResponseTooLargeException(maximumBytes, total);
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        var bytes = destination.ToArray();
        var bufferedContent = new ByteArrayContent(bytes);
        foreach (var header in originalContent.Headers)
        {
            bufferedContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        response.Content = bufferedContent;
        originalContent.Dispose();
        return bytes;
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
