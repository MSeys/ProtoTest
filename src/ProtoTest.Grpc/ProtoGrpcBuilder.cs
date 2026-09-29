namespace ProtoTest.Grpc;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Http;

public sealed class ProtoGrpcBuilder
{
    /// <summary>Stable protocol key gRPC clients are registered and resolved under.</summary>
    public const string ProtocolName = "Grpc";

    /// <summary>The protocol's identity: names, trace source, observation kind and coverage category.</summary>
    internal static readonly ProtoProtocol Protocol = new(
        ProtocolName, "gRPC", "ProtoTest.Grpc", "grpc.response", "gRPC");

    /// <summary>
    /// The observation kind a failed call records. It is deliberately not the response kind: a call that
    /// failed was attempted, not covered, so the coverage collector ignores it.
    /// </summary>
    internal const string FailureObservationKind = "grpc.failure";

    /// <summary>The observation kind a successful shape assertion records; service coverage consumes
    /// the <c>grpc.response</c> kind instead, so this is trace evidence, not coverage.</summary>
    internal const string ShapeObservationKind = "grpc.contract.shape";

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
        var (_, registeredName, applicationName) =
            ProtoHttpClientRegistration.RegisterClientName(_application, Protocol.Key, name);
        return Clients.ProtoGrpcClientRegistration.AddClient(
            Services,
            Protocol.Key,
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
        var (_, registeredName, applicationName) =
            ProtoHttpClientRegistration.RegisterClientName(_application, Protocol.Key, name);
        return Clients.ProtoGrpcClientRegistration.AddClient(
            Services,
            Protocol.Key,
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
