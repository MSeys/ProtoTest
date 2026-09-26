namespace ProtoTest.Grpc.Clients;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoTest.Core;
using ProtoTest.Http;

/// <summary>
/// Registers the named gRPC client and its initializer, so Core creates the channel during setup the
/// same way it creates every other client. Mirrors the HTTP client registration the other protocols use.
/// </summary>
internal static class ProtoGrpcClientRegistration
{
    /// <summary>Registers a named gRPC client with an explicit or application-resolved address.</summary>
    public static IProtoTargetBuilder AddClient(
        IServiceCollection services,
        string protocolName,
        string name,
        string? address,
        Action<GrpcClientOptions>? configure,
        string? application = null,
        bool allowMissingAddress = false)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (address is not null && !ProtoHttpUri.TryCreateAbsoluteHttpUri(address, out _))
        {
            throw new ArgumentException(
                "A gRPC client address must be an absolute HTTP or HTTPS URI.",
                nameof(address));
        }

        var scopedName = ProtoClientResolution.ScopedName(protocolName, name);
        services.AddSingleton<IProtoClientInitializer>(serviceProvider =>
            new ProtoGrpcClientInitializer(
                protocolName,
                name,
                serviceProvider.GetRequiredKeyedService<GrpcClientOptions>(scopedName),
                address,
                allowMissingAddress: allowMissingAddress,
                application: application));
        RegisterOptions(services, scopedName, configure);
        services.AddSingleton(new ProtoApplicationTarget(name, application ?? name));
        return new ProtoTargetBuilder(name, services);
    }

    /// <summary>Registers a named gRPC client whose address is resolved from per-test context.</summary>
    public static IProtoTargetBuilder AddClient(
        IServiceCollection services,
        string protocolName,
        string name,
        Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>> addressResolver,
        Action<GrpcClientOptions>? configure,
        string? application = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(addressResolver);

        var scopedName = ProtoClientResolution.ScopedName(protocolName, name);
        services.AddSingleton<IProtoClientInitializer>(serviceProvider =>
            new ProtoGrpcClientInitializer(
                protocolName,
                name,
                serviceProvider.GetRequiredKeyedService<GrpcClientOptions>(scopedName),
                allowMissingAddress: true,
                application: application,
                addressResolver: addressResolver));
        RegisterOptions(services, scopedName, configure);
        services.AddSingleton(new ProtoApplicationTarget(name, application ?? name));
        return new ProtoTargetBuilder(name, services);
    }

    /// <summary>
    /// Registers the named client's own options: its callbacks compose in registration order, then the
    /// shared <c>ProtoTest:Grpc:Client</c> section binds over the result, so one client's metadata,
    /// deadline or sensitive keys never leak into another client.
    /// </summary>
    private static void RegisterOptions(
        IServiceCollection services,
        string scopedName,
        Action<GrpcClientOptions>? configure)
    {
        if (configure is not null)
        {
            services.AddKeyedSingleton(scopedName, new ConfigureCallback(configure));
        }

        services.TryAddKeyedSingleton<GrpcClientOptions>(scopedName, (serviceProvider, _) =>
            ProtoOptionsRegistration.Resolve<GrpcClientOptions>(serviceProvider, options =>
            {
                foreach (var callback in serviceProvider.GetKeyedServices<ConfigureCallback>(scopedName))
                {
                    callback.Callback(options);
                }
            }));

        // The unkeyed instance is the run-wide default: the transport-backed client the accessor
        // creates for a target with no registration of its own reads it, and it binds the shared
        // section without any named client's callbacks.
        services.TryAddSingleton(serviceProvider =>
            ProtoOptionsRegistration.Resolve<GrpcClientOptions>(serviceProvider));
    }

    private sealed record ConfigureCallback(Action<GrpcClientOptions> Callback);
}
