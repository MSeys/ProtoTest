namespace ProtoTest.Rest.Internal;

using System.Net.Http;
using ProtoTest.Core;

internal sealed class RestHttpClientInitializer(
    string name,
    string? explicitBaseUrl = null,
    bool allowMissingBaseUrl = false)
    : IProtoClientInitializer<HttpClient>
{
    public string Name { get; } = name;

    public static string GetFactoryName(string clientName) => $"ProtoTest.Rest:{clientName}";

    public Task<bool> TryInitializeAsync(ProtoExecutionContext context, CancellationToken cancellationToken = default)
    {
        var baseUrl = explicitBaseUrl
            ?? context.Configuration[$"ProtoTest:Clients:{Name}:BaseUrl"];

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            if (!allowMissingBaseUrl)
            {
                return Task.FromResult(false);
            }

            var dynamicClient = context.Service<IHttpClientFactory>()
                .CreateClient(GetFactoryName(Name));
            context.RegisterClient(dynamicClient, Name);
            return Task.FromResult(true);
        }

        if (!RestUriBuilder.TryCreateAbsoluteHttpUri(baseUrl, out var baseAddress))
        {
            throw new InvalidOperationException(
                $"The base URL configured for REST client '{Name}' must be an absolute HTTP or HTTPS URI.");
        }

        var clientFactory = context.Service<IHttpClientFactory>();
        var client = clientFactory.CreateClient(GetFactoryName(Name));
        client.BaseAddress = baseAddress;

        context.RegisterClient(client, Name);
        return Task.FromResult(true);
    }
}
