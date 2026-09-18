namespace ProtoTest.Sql.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Sql.EntityFrameworkCore.Internal;

public static class ProtoHostBuilderExtensions
{
    /// <summary>
    /// Registers a <typeparamref name="TContext"/> for every test, built over the connection
    /// <see cref="ProtoTest.Sql"/> owns. Configure the provider with that connection - for example
    /// <c>options.UseNpgsql(services.GetRequiredService&lt;DbConnection&gt;())</c> - so the test's
    /// transaction covers both Entity Framework Core and raw commands.
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
                services.AddDbContext<TContext>(configure, ServiceLifetime.Scoped);
                services.AddSingleton<IProtoTestHook>(new SqlEnlistmentHook<TContext>());
            });
    }
}
