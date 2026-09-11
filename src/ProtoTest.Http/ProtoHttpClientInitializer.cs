namespace ProtoTest.Http;

using ProtoTest.Core;

/// <summary>Initializes a named HTTP client from explicit or per-client BaseUrl configuration.</summary>
public sealed class ProtoHttpClientInitializer(
    string protocolName,
    string name,
    string? explicitBaseUrl = null,
    bool allowMissingBaseUrl = false)
    : IProtoClientInitializer<HttpClient>
{
    public string Name { get; } = name;

    public static string GetFactoryName(string protocolName, string clientName)
        => $"ProtoTest.{protocolName}:{clientName}";

    public static bool TryCreateAbsoluteHttpUri(string value, out Uri? uri)
    {
        var created = Uri.TryCreate(value, UriKind.Absolute, out uri);
        return created && uri!.Scheme is "http" or "https";
    }

    public Task<bool> TryInitializeAsync(ProtoExecutionContext context, CancellationToken cancellationToken = default)
    {
        var baseUrl = explicitBaseUrl ?? context.Configuration[$"ProtoTest:Clients:{Name}:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            if (!allowMissingBaseUrl) return Task.FromResult(false);
            Register(context, baseAddress: null);
            return Task.FromResult(true);
        }

        if (!TryCreateAbsoluteHttpUri(baseUrl, out var baseAddress))
            throw new InvalidOperationException(
                $"The base URL configured for {protocolName} client '{Name}' must be an absolute HTTP or HTTPS URI.");

        Register(context, baseAddress);
        return Task.FromResult(true);
    }

    private void Register(ProtoExecutionContext context, Uri? baseAddress)
    {
        var client = context.Service<IHttpClientFactory>().CreateClient(GetFactoryName(protocolName, Name));
        client.BaseAddress = baseAddress;
        context.RegisterClient(client, Name);
    }
}
