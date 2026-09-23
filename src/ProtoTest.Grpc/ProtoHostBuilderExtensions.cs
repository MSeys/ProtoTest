namespace ProtoTest.Grpc;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoTest.Core;
using ProtoTest.Http;

public static class ProtoHostBuilderExtensions
{
    /// <summary>
    /// Adds the gRPC capability. A repeated call is not a no-op: its <c>configure</c> callback always
    /// runs, so a second call composes more clients while the hook and capability stay registered once.
    /// A call whose configure throws leaves no guard behind, so a later successful call still composes.
    /// </summary>
    public static IProtoHostBuilder AddGrpc(this IProtoHostBuilder builder, Action<ProtoGrpcBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ConfigureServices(services =>
        {
            RegisterInfrastructure(services);
            configure?.Invoke(new ProtoGrpcBuilder(services));
        });

        return builder.AddCapability(ProtoGrpcBuilder.Protocol.Capability);
    }

    /// <summary>
    /// Exposes gRPC under an application. Named clients are registered relative to the application and
    /// take their address from <c>ProtoTest:Applications:{app}:Grpc:Address</c>, falling back to the
    /// application's <c>BaseUrl</c> or in-process transport. A repeated call runs its configure too, so
    /// its clients compose.
    /// </summary>
    public static IProtoApplicationBuilder AddGrpc(
        this IProtoApplicationBuilder application,
        Action<ProtoGrpcBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(application);
        RegisterInfrastructure(application.Services);
        configure?.Invoke(new ProtoGrpcBuilder(application.Services, application));
        return application.AddCapability(ProtoGrpcBuilder.Protocol.Capability);
    }

    /// <summary>Marks one successful AddGrpc call, so the hook is registered once.</summary>
    private sealed class GrpcRegistration;

    private static void RegisterInfrastructure(IServiceCollection services)
    {
        if (!ProtoRegistrationGuard.TryRegisterOnce<GrpcRegistration>(services))
        {
            return;
        }

        services.AddSingleton<IProtoTestHook>(new ProtoHttpAuthLifecycleHook(ProtoGrpcBuilder.Protocol));
    }
}
