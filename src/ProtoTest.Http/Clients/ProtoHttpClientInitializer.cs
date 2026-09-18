namespace ProtoTest.Http;

using Microsoft.Extensions.Configuration;
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

    public static string GetFactoryName(string protocolName, string clientName)
        => $"ProtoTest.{protocolName}:{clientName}";

    public Task<bool> TryInitializeAsync(ProtoExecutionContext context, CancellationToken cancellationToken = default)
    {
        var baseUrl = ResolveBaseUrl(context.Configuration);
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

    private string? ResolveBaseUrl(IConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(explicitBaseUrl))
        {
            return explicitBaseUrl;
        }

        var applicationName = application ?? Name;
        var applicationBase = ProtoApplication.BaseUrl(configuration, applicationName);
        if (string.IsNullOrWhiteSpace(applicationBase))
        {
            return null;
        }

        var endpointPath = endpoint is null ? null : ProtoApplication.Endpoint(configuration, applicationName, endpoint);
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
        var client = context.Service<IHttpClientFactory>().CreateClient(GetFactoryName(protocolName, Name));
        client.BaseAddress = baseAddress;
        context.RegisterClient(client, Name);
        return client;
    }

    /// <summary>
    /// Records the client's state once - address with its path, timeout, where the address came from -
    /// instead of an event per call.
    /// </summary>
    private void TraceConfiguration(ProtoExecutionContext context, HttpClient client, string source)
    {
        var state = new Dictionary<string, string?>
        {
            ["client.name"] = Name,
            ["client.protocol"] = protocolName,
            ["client.type"] = typeof(HttpClient).FullName,
            ["client.timeout_seconds"] = client.Timeout.TotalSeconds.ToString(
                "0.###", System.Globalization.CultureInfo.InvariantCulture),
            ["client.endpoint_source"] = source
        };
        if (client.BaseAddress is not null)
        {
            state["client.base_address"] = SafeAddress(client.BaseAddress);
        }

        context.Trace.SetEntityState(
            ProtoTraceEntityKinds.Client,
            $"client:{typeof(HttpClient).FullName}:{Name}",
            $"HTTP client {Name}",
            state,
            scope: context.TestName);
    }

    private static string SafeAddress(Uri address)
    {
        if (string.IsNullOrEmpty(address.UserInfo))
        {
            return address.ToString();
        }

        return new UriBuilder(address) { UserName = string.Empty, Password = string.Empty }.Uri.ToString();
    }
}
