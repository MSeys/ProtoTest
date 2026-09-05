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
    IRestAuthenticator? defaultAuthenticator
    )
{
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly ProtoExecutionContext _context = context ?? throw new ArgumentNullException(nameof(context));
    private readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);
    private HttpContent? _content;

    /// <summary>
    /// Overrides or sets a explicit authenticator for this request.
    /// </summary>
    public RestRequestBuilder Auth(IRestAuthenticator authenticator)
    {
        defaultAuthenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        return this;
    }

    /// <summary>
    /// Instantiates and sets a custom authenticator via DI and constructor args for this request.
    /// </summary>
    public RestRequestBuilder Auth<TAuth>(params object[] constructorArgs) where TAuth : IRestAuthenticator
    {
        defaultAuthenticator = ActivatorUtilities.CreateInstance<TAuth>(_context.Services, constructorArgs);
        return this;
    }

    /// <summary>
    /// Adds a custom header to the request.
    /// </summary>
    public RestRequestBuilder Header(string name, string value)
    {
        _headers[name] = value;
        return this;
    }

    /// <summary>
    /// Sets a JSON body payload using system options or custom options.
    /// </summary>
    public RestRequestBuilder Body(object payload, JsonSerializerOptions? options = null)
    {
        var json = JsonSerializer.Serialize(payload, options);
        _content = new StringContent(json, Encoding.UTF8, "application/json");
        return this;
    }

    /// <summary>
    /// Sets a raw string body with custom content type.
    /// </summary>
    public RestRequestBuilder Body(string rawContent, string mediaType = "text/plain")
    {
        _content = new StringContent(rawContent, Encoding.UTF8, mediaType);
        return this;
    }

    // --- HTTP Execution Methods ---

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

        if (_content != null)
        {
            request.Content = _content;
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
        using var responseMessage = await _httpClient.SendAsync(request, ct);
        stopwatch.Stop();

        var bodyString = await responseMessage.Content.ReadAsStringAsync(ct);
        return new RestResponse(responseMessage, bodyString, stopwatch.Elapsed);
    }
}