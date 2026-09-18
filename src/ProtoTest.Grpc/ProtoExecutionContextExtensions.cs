namespace ProtoTest.Grpc;

using ProtoTest.Core;

public static class ProtoExecutionContextExtensions
{
    /// <summary>
    /// Entry point for gRPC calls. Inside an <c>[Application]</c> the default or bound gRPC client is
    /// used unless <paramref name="clientName"/> names another, exactly like <c>Rest()</c> and
    /// <c>GraphQL()</c>.
    /// </summary>
    public static ProtoGrpcClient Grpc(this ProtoExecutionContext context, string? clientName = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var application = ProtoApplicationResolution.ResolveApplicationName(context);
        var selected = clientName ?? ProtoApplicationResolution.ResolveClientName(context, "Grpc", fallback: "Default");
        var resolvedName = application is null ? selected : $"{application}:{selected}";

        var client = context.TryClient<ProtoGrpcClient>(resolvedName);
        if (client is null)
        {
            // No initialized client (for example a target resolved only at call time): back it with the
            // application's in-process transport, the same fallback the HTTP-based protocols use. The
            // fallback client is registered so every call shares one channel and teardown releases it.
            var transport = ProtoApplicationResolution.ResolveTransportClient(context, application);
            if (transport is null)
            {
                throw new InvalidOperationException(
                    $"No gRPC client '{resolvedName}' is registered. Register it under the application, " +
                    $"back the application with AddAspNetCoreServer, or set " +
                    $"'ProtoTest:Applications:{application}:Grpc:Address'.");
            }

            client = ProtoGrpcClient.ForTransport(context, resolvedName, transport);
            context.RegisterClient(client, resolvedName);

            context.Trace.WriteEvent(
                "grpc.client.resolve",
                $"gRPC client · {selected}",
                "ProtoTest.Grpc",
                outcome: ProtoTraceOutcome.Succeeded,
                attributes: new Dictionary<string, string?>
                {
                    ["client.name"] = selected,
                    ["application.name"] = application,
                    ["client.source_name"] = resolvedName
                });
        }

        return client;
    }
}
