namespace ProtoTest.Rest;

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Rest.Internal;

public sealed class RestRequestBuilder(
    HttpClient httpClient,
    ProtoExecutionContext context,
    string targetName,
    IRestAuthenticator? defaultAuthenticator
)
{
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly ProtoExecutionContext _context = context ?? throw new ArgumentNullException(nameof(context));
    private readonly string _targetName = targetName ?? throw new ArgumentNullException(nameof(targetName));
    private readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);
    private HttpContent? _content;

    public RestRequestBuilder Auth(IRestAuthenticator authenticator)
    {
        defaultAuthenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        return this;
    }

    public RestRequestBuilder Auth<TAuth>(params object[] constructorArgs) where TAuth : IRestAuthenticator
    {
        defaultAuthenticator = ActivatorUtilities.CreateInstance<TAuth>(_context.Services, constructorArgs);
        return this;
    }

    public RestRequestBuilder Header(string name, string value)
    {
        _headers[name] = value;
        return this;
    }

    public RestRequestBuilder Body(object payload, JsonSerializerOptions? options = null)
    {
        var json = JsonSerializer.Serialize(payload, options);
        _content = new StringContent(json, Encoding.UTF8, "application/json");
        return this;
    }

    public RestRequestBuilder Body(string rawContent, string mediaType = "text/plain")
    {
        _content = new StringContent(rawContent, Encoding.UTF8, mediaType);
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

    private async Task<RestResponse> SendAsync(HttpMethod method, string routeTemplate, object? routeAndQueryParams, CancellationToken ct)
    {
        var finalUrl = RouteAndQueryParser.BuildUrl(routeTemplate, routeAndQueryParams);
        using var request = new HttpRequestMessage(method, finalUrl);
        var attachmentOptions = _context.TryService<RestAttachmentOptions>();
        var attachmentNumber = attachmentOptions is null
            ? (int?)null
            : attachmentOptions.NextAttachmentNumber();
        var attachmentPrefix = attachmentNumber is null ? null : $"rest-{attachmentNumber:00}";
        var attachmentDescription = $"{method.Method.ToUpperInvariant()} {routeTemplate}";

        if (_content != null)
        {
            request.Content = _content;

            if (attachmentOptions?.CaptureRequestBodies == true)
            {
                var requestBody = await _content.ReadAsStringAsync(ct);
                _context.AddAttachment(
                    $"{attachmentPrefix}-request",
                    requestBody,
                    _content.Headers.ContentType?.MediaType ?? "text/plain",
                    attachmentDescription);
            }
        }

        foreach (var (key, value) in _headers)
        {
            request.Headers.TryAddWithoutValidation(key, value);
        }

        if (defaultAuthenticator != null)
        {
            await defaultAuthenticator.AuthenticateAsync(request, ct);
        }

        var stopwatch = Stopwatch.StartNew();
        var responseMessage = await _httpClient.SendAsync(request, ct);
        stopwatch.Stop();

        try
        {
            var bodyString = await responseMessage.Content.ReadAsStringAsync(ct);

            if (attachmentOptions?.CaptureResponses == true)
            {
                _context.AddAttachment(
                    $"{attachmentPrefix}-response",
                    bodyString,
                    responseMessage.Content.Headers.ContentType?.MediaType ?? "text/plain",
                    $"{attachmentDescription} returned {(int)responseMessage.StatusCode} ({responseMessage.StatusCode})");
            }

            var headersDict = responseMessage.Headers
                .ToDictionary(h => h.Key, h => string.Join(", ", h.Value), StringComparer.OrdinalIgnoreCase);

            var routeIdentifier = $"{method.Method.ToUpperInvariant()} {routeTemplate}";

            _context.RecordObservation(new ProtoObservation(
                TargetName: _targetName,
                Kind: "http.response",
                Identifier: routeIdentifier,
                Data: new RestHitData(
                    Method: method.Method,
                    RouteTemplate: routeTemplate,
                    StatusCode: (int)responseMessage.StatusCode,
                    ResponseBody: bodyString,
                    Headers: headersDict
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
                attachmentPrefix
            );
        }
        catch
        {
            responseMessage.Dispose();
            throw;
        }
    }
}
