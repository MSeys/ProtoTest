namespace ProtoTest.Data;

using ProtoTest.Data.Internal;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoTest.Core;
using System.Runtime.CompilerServices;

public static class ProtoHostBuilderExtensions
{
    private static readonly ConditionalWeakTable<IProtoHostBuilder, ProtoDataRegistry> Registries = new();

    /// <summary>Adds deterministic test-data construction to the ProtoTest host.</summary>
    public static IProtoHostBuilder AddData(
        this IProtoHostBuilder builder,
        Action<ProtoDataConfiguration>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var registry = Registries.GetValue(builder, static _ => new ProtoDataRegistry());
        configure?.Invoke(new ProtoDataConfiguration(registry));

        return builder
            .AddCapability(new ProtoCapabilityDescriptor("Data", ProtoCapabilityKinds.Data, "ProtoTest.Data"))
            .ConfigureServices(services =>
            {
                services.TryAddSingleton(registry);
                services.TryAddScoped<IProtoData, ProtoDataService>();
            });
    }

    /// <summary>Registers the single application-specific creation route for a data type.</summary>
    public static IProtoHostBuilder AddDataProvisioner<T, TProvisioner>(this IProtoHostBuilder builder)
        where TProvisioner : class, IProtoDataProvisioner<T>
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.ConfigureServices(services =>
            services.AddScoped<IProtoDataProvisioner<T, T>, TProvisioner>());
    }

    /// <summary>Registers a creation route whose application result differs from its input data.</summary>
    public static IProtoHostBuilder AddDataProvisioner<TInput, TResult, TProvisioner>(this IProtoHostBuilder builder)
        where TProvisioner : class, IProtoDataProvisioner<TInput, TResult>
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.ConfigureServices(services =>
            services.AddScoped<IProtoDataProvisioner<TInput, TResult>, TProvisioner>());
    }
}
