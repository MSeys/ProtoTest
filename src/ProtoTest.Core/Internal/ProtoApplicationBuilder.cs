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
internal sealed class ProtoApplicationBuilder : IProtoApplicationBuilder, IProtoComposableBuilder
{
    private readonly Func<bool>? _isBuilt;
    private readonly ProtoProviderChainBuilder _providers;
    private readonly IProtoHostBuilder _host;
    private readonly List<ProtoApplicationClient> _clients;

    public ProtoApplicationBuilder(
        string applicationName,
        IProtoHostBuilder host,
        IServiceCollection services,
        List<ProtoApplicationClient> clients,
        Func<bool>? isBuilt = null)
    {
        ApplicationName = applicationName;
        _host = host;
        _clients = clients;
        _isBuilt = isBuilt;
        Services = services;
        _providers = new ProtoProviderChainBuilder(
            applicationName,
            [ProtoApplication.SettingsKey(applicationName, "BaseUrl")],
            services,
            ThrowIfBuilt);
    }

    public string ApplicationName { get; }

    public IServiceCollection Services { get; }

    public IProtoHostBuilder Host => _host;

    public IProtoProviderChainBuilder Providers => _providers;

    public void RegisterClient(string protocolName, string clientName)
    {
        ThrowIfBuilt();
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
        _clients.Add(new ProtoApplicationClient(protocolName, clientName));
    }

    /// <summary>
    /// Registers the application's target once its providers are declared, so the chain resolves at
    /// build and the winner's services and capabilities are the ones the run gets. An application
    /// that declared no provider registers no target: it keeps the plain configured-address behavior.
    /// </summary>
    public void RegisterTarget()
    {
        if (_providers.Providers.Count == 0)
        {
            return;
        }

        ProtoInfrastructureExtensions.RegisterTarget(
            _host,
            ApplicationName,
            _providers.Keys,
            _providers.Providers,
            _providers.ResolveAfterTargets);
    }

    public void ThrowIfBuilt()
    {
        if (_isBuilt?.Invoke() == true)
        {
            throw new InvalidOperationException(ProtoHostBuilder.BuiltMessage);
        }
    }
}
