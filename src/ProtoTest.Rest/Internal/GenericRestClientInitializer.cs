namespace ProtoTest.Rest.Internal;

using System.Net.Http;
using ProtoTest.Core;

internal sealed class GenericRestClientInitializer(string name, string? explicitBaseUrl = null) : IProtoClientInitializer
{
    public string Name { get; } = name;

    public Task InitializeAsync(ProtoExecutionContext context, CancellationToken cancellationToken = default)
    {
        var baseUrl = explicitBaseUrl
            ?? context.Configuration[$"ProtoTest:Clients:{Name}:BaseUrl"];

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException(
                $"No BaseUrl configured for REST client '{Name}'. " +
                $"Provide it in AddRestClient(\"{Name}\", baseUrl) or set 'ProtoTest:Clients:{Name}:BaseUrl' in configuration.");
        }

        var client = new HttpClient
        {
            BaseAddress = new Uri(baseUrl)
        };

        context.RegisterClient(client, Name);
        return Task.CompletedTask;
    }
}