namespace ProtoTest.Rest.Internal;

using System.Net.Http;
using ProtoTest.Core;

internal sealed class GenericRestClientInitializer(string name, string? explicitBaseUrl = null)
    : IProtoClientInitializer<HttpClient>
{
    public string Name { get; } = name;

    public Task<bool> TryInitializeAsync(ProtoExecutionContext context, CancellationToken cancellationToken = default)
    {
        var baseUrl = explicitBaseUrl
            ?? context.Configuration[$"ProtoTest:Clients:{Name}:BaseUrl"];

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return Task.FromResult(false);
        }

        var client = new HttpClient
        {
            BaseAddress = new Uri(baseUrl)
        };

        context.RegisterClient(client, Name);
        return Task.FromResult(true);
    }
}
