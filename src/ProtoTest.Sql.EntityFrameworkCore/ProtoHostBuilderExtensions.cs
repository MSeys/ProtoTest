namespace ProtoTest.Sql.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoTest.Core;
using ProtoTest.Sql.EntityFrameworkCore.Internal;

public static class ProtoHostBuilderExtensions
{
    /// <summary>
    /// Registers a <typeparamref name="TContext"/> for every test, built over the connection
    /// <see cref="ProtoTest.Sql"/> owns. Configure the provider with that connection - for example
    /// <c>options.UseNpgsql(services.GetRequiredService&lt;DbConnection&gt;())</c> - so the test's
    /// transaction covers both Entity Framework Core and raw commands. A second registration for the same
    /// context is a no-op; two different contexts are different registrations and both apply. A context the
    /// host registered before this call keeps the host's configuration but is still enlisted in the test's
    /// transaction; Entity Framework Core keeps only the first options registration, so a host
    /// <c>AddDbContext</c> called after this one has its options delegate dropped and the host's context
    /// must be registered first.
    /// </summary>
    public static IProtoHostBuilder AddEntityFrameworkCore<TContext>(
        this IProtoHostBuilder builder,
        Action<IServiceProvider, DbContextOptionsBuilder> configure)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        return builder
            .AddCapability(new ProtoCapabilityDescriptor(
                "Entity Framework Core", ProtoCapabilityKinds.Store, "ProtoTest.Sql.EntityFrameworkCore"))
            .ConfigureServices(services =>
            {
                // A ProtoTest-owned marker gates duplicate registration; DbContextOptions alone is not
                // that marker because the host may have registered the context before this call.
                if (services.Any(descriptor =>
                        descriptor.ServiceType == typeof(ProtoEntityFrameworkCoreRegistration<TContext>)))
                {
                    return;
                }

                services.AddSingleton<ProtoEntityFrameworkCoreRegistration<TContext>>();

                // Entity Framework Core keeps the first DbContextOptions<TContext> registration and
                // drops later options delegates, so this check makes the call order explicit: when the
                // host registered its context first its configuration is preserved, and when this call
                // comes first the host's later AddDbContext has no effect. SqlEnlistmentHook validates
                // the resolved connection either way, so an ambiguous order fails with guidance.
                if (!services.Any(descriptor => descriptor.ServiceType == typeof(DbContextOptions<TContext>)))
                {
                    services.AddDbContext<TContext>(configure, ServiceLifetime.Scoped);
                }

                services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtoTestHook, SqlEnlistmentHook<TContext>>());
            });
    }

    /// <summary>Marks that ProtoTest has already registered an enlistment hook for this context.</summary>
    private sealed class ProtoEntityFrameworkCoreRegistration<TContext>;
}
