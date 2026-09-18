namespace ProtoTest.Grpc.Clients;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Http;

/// <summary>
/// Registers the named gRPC client and its initializer, so Core creates the channel during setup the
/// same way it creates every other client. Mirrors the HTTP client registration the other protocols use.
/// </summary>
public static class ProtoGrpcClientRegistration
{
    /// <summary>Registers a named gRPC client with an explicit or application-resolved address.</summary>
    public static IProtoTargetBuilder AddClient(
        IServiceCollection services,
        string protocolName,
        string name,
        string? address,
        Action<ProtoGrpcClientOptions>? configure,
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

        services.AddSingleton<IProtoClientInitializer>(serviceProvider =>
        {
            var options = new ProtoGrpcClientOptions();
            configure?.Invoke(options);
            options.BindFromConfiguration(serviceProvider.GetRequiredService<IConfiguration>());
            return new ProtoGrpcClientInitializer(
                protocolName,
                name,
                options,
                address,
                allowMissingAddress: allowMissingAddress,
                application: application);
        });
        services.AddSingleton(new ProtoApplicationTarget(name, application ?? name));
        return new ProtoGrpcTargetBuilder(name, services);
    }

    /// <summary>Registers a named gRPC client whose address is resolved from per-test context.</summary>
    public static IProtoTargetBuilder AddClient(
        IServiceCollection services,
        string protocolName,
        string name,
        Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>> addressResolver,
        Action<ProtoGrpcClientOptions>? configure,
        string? application = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(addressResolver);

        services.AddSingleton<IProtoClientInitializer>(serviceProvider =>
        {
            var options = new ProtoGrpcClientOptions();
            configure?.Invoke(options);
            options.BindFromConfiguration(serviceProvider.GetRequiredService<IConfiguration>());
            return new ProtoGrpcClientInitializer(
                protocolName,
                name,
                options,
                allowMissingAddress: true,
                application: application,
                addressResolver: addressResolver);
        });
        services.AddSingleton(new ProtoGrpcAddressRegistration(protocolName, name, addressResolver));
        services.AddSingleton(new ProtoApplicationTarget(name, application ?? name));
        return new ProtoGrpcTargetBuilder(name, services);
    }
}
