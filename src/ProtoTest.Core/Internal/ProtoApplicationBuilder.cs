namespace ProtoTest.Core.Internal;

using Microsoft.Extensions.DependencyInjection;

/// <summary>Collects an application's protocol client registrations while <c>AddApplication</c> is configured.</summary>
internal sealed class ProtoApplicationBuilder(
    string applicationName,
    IServiceCollection services,
    List<ProtoApplicationClient> clients) : IProtoApplicationBuilder
{
    public string ApplicationName { get; } = applicationName;
    public IServiceCollection Services { get; } = services;

    public void RegisterClient(string protocolName, string clientName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
        clients.Add(new ProtoApplicationClient(protocolName, clientName));
    }
}
