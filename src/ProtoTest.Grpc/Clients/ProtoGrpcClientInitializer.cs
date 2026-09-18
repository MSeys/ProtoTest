namespace ProtoTest.Grpc.Clients;

using global::Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using ProtoTest.Core;

/// <summary>
/// Initializes a named gRPC client from an explicit address, or from the application it belongs to: the
/// application's <c>Grpc:Address</c>, its base address, or its in-process transport. The channel is
/// created when the client is first used, so an application whose test server starts later still works.
/// </summary>
public sealed class ProtoGrpcClientInitializer(
    string protocolName,
    string name,
    ProtoGrpcClientOptions options,
    string? explicitAddress = null,
    bool allowMissingAddress = false,
    string? application = null,
    Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>>? addressResolver = null)
    : IProtoClientInitializer<ProtoGrpcClient>
{
    public string Name { get; } = name;

    public Task<bool> TryInitializeAsync(ProtoExecutionContext context, CancellationToken cancellationToken = default)
    {
        options.BindFromConfiguration(context.Configuration);
        var configured = ResolveConfiguredAddress(context.Configuration);
        if (configured is null && !allowMissingAddress)
        {
            return Task.FromResult(false);
        }

        var source = configured is null
            ? "deferred"
            : explicitAddress is null ? "configuration" : "registration";
        var client = new ProtoGrpcClient(context, Name, options, (_, ct) => CreateChannelAsync(
            context,
            configured,
            addressResolver,
            application,
            ct));
        context.RegisterClient(client, Name);
        TraceConfiguration(context, configured, source);
        return Task.FromResult(true);
    }

    private Uri? ResolveConfiguredAddress(IConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(explicitAddress))
        {
            return new Uri(explicitAddress);
        }

        var applicationName = application ?? Name;
        var configured = ProtoApplication.Section(configuration, applicationName)["Grpc:Address"]
            ?? ProtoApplication.BaseUrl(configuration, applicationName);
        return string.IsNullOrWhiteSpace(configured) ? null : new Uri(configured);
    }

    private static async ValueTask<GrpcChannel> CreateChannelAsync(
        ProtoExecutionContext context,
        Uri? configured,
        Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>>? addressResolver,
        string? application,
        CancellationToken cancellationToken)
    {
        var transport = application is null
            ? null
            : ProtoApplicationResolution.ResolveTransportClient(context, application);
        var address = configured
            ?? (addressResolver is null ? null : await addressResolver(context, cancellationToken))
            ?? transport?.BaseAddress;
        if (address is null)
        {
            throw new InvalidOperationException(
                "No gRPC address is available for this client. Pass one to AddClient, set " +
                "'ProtoTest:Applications:{app}:Grpc:Address' or 'BaseUrl', or back the application with " +
                "AddAspNetCoreServer.");
        }

        // An address that came from the application's in-process transport runs over the test server.
        return configured is null && addressResolver is null && transport is not null
            ? GrpcChannel.ForAddress(address, new GrpcChannelOptions
            {
                HttpHandler = new Internal.GrpcChannelForwardingHandler(transport)
            })
            : GrpcChannel.ForAddress(address);
    }

    /// <summary>
    /// Records the client's state once - address, where it came from - instead of an event per call.
    /// The <c>client.initialize</c> operation and <c>client.initializer</c> field come from Core.
    /// </summary>
    private void TraceConfiguration(ProtoExecutionContext context, Uri? address, string source)
    {
        var state = new Dictionary<string, string?>
        {
            ["client.name"] = Name,
            ["client.protocol"] = protocolName,
            ["client.type"] = typeof(ProtoGrpcClient).FullName,
            ["client.endpoint_source"] = source
        };
        if (address is not null)
        {
            state["client.address"] = address.ToString();
        }

        context.Trace.SetEntityState(
            ProtoTraceEntityKinds.Client,
            $"client:{typeof(ProtoGrpcClient).FullName}:{Name}",
            $"gRPC client {Name}",
            state,
            scope: context.TestName);
    }
}
