namespace ProtoTest.Grpc;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Http;

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
    /// application's <c>BaseUrl</c> or its in-process transport. The name may be omitted for an
    /// application's only gRPC client.
    /// </summary>
    public IProtoTargetBuilder AddClient(
        string? name = null,
        string? address = null,
        Action<GrpcClientOptions>? configure = null)
    {
        var clientName = name ?? "Default";
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
        var applicationName = _application?.ApplicationName;
        var registeredName = ProtoHttpClientRegistration.Qualify(clientName, applicationName);
        _application?.RegisterClient("Grpc", clientName);
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
        string? name,
        Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>> addressResolver,
        Action<GrpcClientOptions>? configure = null)
    {
        var clientName = name ?? "Default";
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
        var applicationName = _application?.ApplicationName;
        var registeredName = ProtoHttpClientRegistration.Qualify(clientName, applicationName);
        _application?.RegisterClient("Grpc", clientName);
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
        string? name,
        Func<ProtoExecutionContext, Uri> addressResolver,
        Action<GrpcClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(addressResolver);
        return AddClient(name, (context, _) => ValueTask.FromResult(addressResolver(context)), configure);
    }

    /// <summary>
    /// Enables automatic request and response message attachments, sanitized and redacted. Repeated
    /// calls compose: every callback runs in registration order and configuration binds over the result.
    /// </summary>
    public ProtoGrpcBuilder CaptureAttachments(Action<GrpcAttachmentOptions>? configure = null)
    {
        ProtoOptionsRegistration.Configure(Services, () => new GrpcAttachmentOptions(), configure);
        return this;
    }
}
