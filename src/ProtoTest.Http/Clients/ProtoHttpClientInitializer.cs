namespace ProtoTest.Http;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using ProtoTest.Core;

/// <summary>
/// Initializes a named HTTP client from an explicit URL, or from the application the target belongs to:
/// the application's base address, optionally combined with one of its named endpoint paths.
/// </summary>
public sealed class ProtoHttpClientInitializer(
    string protocolName,
    string name,
    string? explicitBaseUrl = null,
    bool allowMissingBaseUrl = false,
    string? application = null,
    string? endpoint = null)
    : IProtoClientInitializer<HttpClient>
{
    public string Name { get; } = name;

    public string Protocol => protocolName;

    private string ScopedName => ProtoClientResolution.ScopedName(Protocol, Name);

    public static string GetFactoryName(string protocolName, string clientName)
        => $"ProtoTest.{protocolName}:{clientName}";

    public Task<bool> TryInitializeAsync(ProtoExecutionContext context)
    {
        var baseUrl = ResolveBaseUrl(context);
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            if (!allowMissingBaseUrl) return Task.FromResult(false);
            TraceConfiguration(context, Register(context, baseAddress: null), "deferred");
            return Task.FromResult(true);
        }

        if (!ProtoHttpUri.TryCreateAbsoluteHttpUri(baseUrl, out var baseAddress))
            throw new InvalidOperationException(
                $"The base URL configured for {protocolName} client '{Name}' must be an absolute HTTP or HTTPS URI.");

        TraceConfiguration(context, Register(context, baseAddress), explicitBaseUrl is null ? "configuration" : "registration");
        return Task.FromResult(true);
    }

    private string? ResolveBaseUrl(ProtoExecutionContext context)
    {
        if (!string.IsNullOrWhiteSpace(explicitBaseUrl))
        {
            return explicitBaseUrl;
        }

        var applicationName = application ?? Name;
        // The one application-address precedence: a started piece's published address wins over
        // configuration, so the client talks to the process the run started.
        var applicationBase = ProtoApplication.BaseUrl(context, applicationName);
        if (string.IsNullOrWhiteSpace(applicationBase))
        {
            return null;
        }

        var endpointPath = endpoint is null ? null : ProtoApplication.Endpoint(context.Configuration, applicationName, endpoint);
        if (string.IsNullOrWhiteSpace(endpointPath))
        {
            return applicationBase;
        }

        if (!Uri.TryCreate(applicationBase, UriKind.Absolute, out var origin))
        {
            throw new InvalidOperationException(
                $"The base URL '{applicationBase}' for application '{applicationName}' must be an absolute URI.");
        }

        return new Uri(origin, endpointPath).ToString();
    }

    private HttpClient Register(ProtoExecutionContext context, Uri? baseAddress)
    {
        var factoryName = GetFactoryName(protocolName, Name);
        // A resolved address means the requests leave this process, so the primary handler's cookie
        // container carries the test's sign-in state. The factory pools one handler per client name
        // for the whole handler lifetime, which would hand that state to every parallel test of the
        // run; build the named client's own pipeline over a handler this test owns instead. Without an
        // address the registered client only backs the application's in-process transport, and the
        // pooled client is what serves it.
        var client = baseAddress is null
            ? context.Service<IHttpClientFactory>().CreateClient(factoryName)
            : CreatePerTestClient(context, factoryName);
        client.BaseAddress = baseAddress;
        context.RegisterClient(client, ScopedName);
        return client;
    }

    /// <summary>
    /// Rebuilds the named client's pipeline the way <see cref="IHttpClientFactory"/> would - the named
    /// options' handler and client actions, composed with the registered builder filters - but over a
    /// fresh primary handler, so its cookies and connections live and die with the test.
    /// </summary>
    private static HttpClient CreatePerTestClient(ProtoExecutionContext context, string factoryName)
    {
        var options = context.Service<IOptionsMonitor<HttpClientFactoryOptions>>().Get(factoryName);
        var builder = context.Service<HttpMessageHandlerBuilder>();
        builder.Name = factoryName;

        Action<HttpMessageHandlerBuilder> configure = target =>
        {
            foreach (var action in options.HttpMessageHandlerBuilderActions)
            {
                action(target);
            }
        };
        var filters = context.Services.GetServices<IHttpMessageHandlerBuilderFilter>().ToArray();
        for (var index = filters.Length - 1; index >= 0; index--)
        {
            configure = filters[index].Configure(configure);
        }

        configure(builder);
        var client = new HttpClient(builder.Build(), disposeHandler: true);
        foreach (var action in options.HttpClientActions)
        {
            action(client);
        }

        return client;
    }

    /// <summary>
    /// Records the client's state once - address with its path, timeout, where the address came from -
    /// instead of an event per call.
    /// </summary>
    private void TraceConfiguration(ProtoExecutionContext context, HttpClient client, string source)
    {
        var details = new Dictionary<string, string?>
        {
            ["client.timeout_seconds"] = client.Timeout.TotalSeconds.ToString(
                "0.###", System.Globalization.CultureInfo.InvariantCulture)
        };
        if (client.BaseAddress is not null)
        {
            details["client.base_address"] = SafeAddress(client.BaseAddress);
        }

        ProtoClientTraceState.SetConfiguration(
            context,
            typeof(HttpClient),
            protocolName,
            Name,
            ScopedName,
            $"HTTP client {Name}",
            source,
            details);
    }

    private static string SafeAddress(Uri address)
        => ProtoUriSanitizer.ForDiagnostics(address.OriginalString) ?? address.OriginalString;
}
