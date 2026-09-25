namespace ProtoTest.Core.Internal;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The composition rule a builder that hands out a service collection shares with
/// <see cref="ProtoHostBuilder"/>: once the host is built, no registration entry may mutate the
/// collection any more.
/// </summary>
internal interface IProtoComposableBuilder
{
    void ThrowIfBuilt();
}

/// <summary>Collects an application's protocol client registrations while <c>AddApplication</c> is configured.</summary>
internal sealed class ProtoApplicationBuilder(
    string applicationName,
    IServiceCollection services,
    List<ProtoApplicationClient> clients,
    Func<bool>? isBuilt = null) : IProtoApplicationBuilder, IProtoComposableBuilder
{
    public string ApplicationName { get; } = applicationName;
    public IServiceCollection Services { get; } = services;

    public void RegisterClient(string protocolName, string clientName)
    {
        ThrowIfBuilt();
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
        clients.Add(new ProtoApplicationClient(protocolName, clientName));
    }

    public void ThrowIfBuilt()
    {
        if (isBuilt?.Invoke() == true)
        {
            throw new InvalidOperationException(ProtoHostBuilder.BuiltMessage);
        }
    }
}
