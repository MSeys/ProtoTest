namespace ProtoTest.Grpc;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

public sealed class ProtoGrpcBuilder
{
    private readonly IProtoApplicationBuilder? _application;

    internal ProtoGrpcBuilder(IServiceCollection services, IProtoApplicationBuilder? application = null)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        _application = application;
    }

    public IServiceCollection Services { get; }

    /// <summary>
    /// Registers a named gRPC client. Inside <c>AddApplication</c> the client belongs to that application
    /// and takes its address from <c>ProtoTest:Applications:{app}:Grpc:Address</c>, falling back to the
    /// application's <c>BaseUrl</c> or its in-process transport.
    /// </summary>
    public IProtoTargetBuilder AddClient(
        string name = "Default",
        string? address = null,
        Action<ProtoGrpcClientOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var applicationName = _application?.ApplicationName;
        var registeredName = Qualify(name, applicationName);
        _application?.RegisterClient("Grpc", name);
        return Clients.ProtoGrpcClientRegistration.AddClient(
            Services,
            "Grpc",
            registeredName,
            address,
            configure,
            applicationName,
            allowMissingAddress: applicationName is not null && address is null);
    }

    /// <summary>Adds a client whose absolute address is resolved from per-test context.</summary>
    public IProtoTargetBuilder AddClient(
        string name,
        Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>> addressResolver,
        Action<ProtoGrpcClientOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var applicationName = _application?.ApplicationName;
        var registeredName = Qualify(name, applicationName);
        _application?.RegisterClient("Grpc", name);
        return Clients.ProtoGrpcClientRegistration.AddClient(
            Services,
            "Grpc",
            registeredName,
            addressResolver,
            configure,
            applicationName);
    }

    /// <summary>Adds a client whose absolute address is read from per-test context.</summary>
    public IProtoTargetBuilder AddClient(
        string name,
        Func<ProtoExecutionContext, Uri> addressResolver,
        Action<ProtoGrpcClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(addressResolver);
        return AddClient(name, (context, _) => ValueTask.FromResult(addressResolver(context)), configure);
    }

    private static string Qualify(string name, string? applicationName)
        => applicationName is null ? name : $"{applicationName}:{name}";
}
