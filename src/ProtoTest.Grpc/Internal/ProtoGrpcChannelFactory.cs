namespace ProtoTest.Grpc.Internal;

using global::Grpc.Net.Client;
using ProtoTest.Core;

/// <summary>
/// The one place a gRPC channel is built. A channel over an in-process transport needs that transport's
/// base address; a transport without one fails with the message that names the address sources instead
/// of dialing an address the caller never chose.
/// </summary>
internal static class ProtoGrpcChannelFactory
{
    /// <summary>Builds the channel that forwards over the application's in-process transport.</summary>
    public static GrpcChannel ForTransport(HttpClient transport, string clientName, string? application)
        => GrpcChannel.ForAddress(
            transport.BaseAddress ?? throw MissingAddress(clientName, application),
            new GrpcChannelOptions { HttpHandler = new GrpcChannelForwardingHandler(transport) });

    /// <summary>Builds the channel over an address resolved from registration, config or a resolver.</summary>
    public static GrpcChannel ForAddress(Uri address) => GrpcChannel.ForAddress(address);

    /// <summary>The failure a client hits when no address can be resolved, naming every address source.</summary>
    public static InvalidOperationException MissingAddress(string clientName, string? application)
    {
        var scope = application is null
            ? $"client '{clientName}'"
            : $"client '{clientName}' in application '{application}'";
        var applicationName = application ?? clientName;
        return new InvalidOperationException(
            $"No gRPC address is available for {scope}. Pass one to AddClient, set " +
            $"'{ProtoApplication.SectionPath}:{applicationName}:Grpc:Address' or 'BaseUrl', or back " +
            "the application with AddAspNetCoreServer.");
    }
}
