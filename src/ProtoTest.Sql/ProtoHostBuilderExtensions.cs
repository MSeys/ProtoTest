namespace ProtoTest.Sql;

using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoTest.Core;
using ProtoTest.Sql.Internal;

public static class ProtoHostBuilderExtensions
{
    /// <summary>Marks one successful AddSql call; the presence of SqlOptions alone means nothing.</summary>
    private sealed class SqlRegistration;

    /// <summary>The Store capability this integration declares; the isolation guard applies while it is present.</summary>
    internal const string CapabilityName = "SQL";

    /// <summary>
    /// Gives every test its own <see cref="DbConnection"/>, opened and owned as a resource. The factory
    /// runs inside the test scope, so it can read a container's connection string from per-test state.
    /// Any access technology can share that connection - Entity Framework Core, Dapper or raw ADO.NET.
    /// A second registration is a no-op: the first call's factory and options win. A host that registered
    /// its own <see cref="SqlOptions"/> keeps them and the rest of the integration composes around them.
    /// </summary>
    /// <remarks>
    /// Declare <see cref="SqlOptions.AddressKeys"/> in <paramref name="configure"/> to make the
    /// integration inert when no declared key can provide the connection: the Store capability is then
    /// absent, the connection is not opened during setup, and the accessors throw naming the keys.
    /// </remarks>
    public static IProtoHostBuilder AddSql(
        this IProtoHostBuilder builder,
        Func<IServiceProvider, DbConnection> connectionFactory,
        Action<SqlOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(connectionFactory);

        var capability = new ProtoCapabilityDescriptor(
            CapabilityName, ProtoCapabilityKinds.Store, ProtoSqlSession.TraceSource);
        return builder.ConfigureServices(services =>
        {
            // The marker, not the presence of SqlOptions, decides whether SQL already registered: a
            // host that registered its own options keeps them (the options registration is TryAdd)
            // instead of silently disabling the connection, session, hooks and guard.
            if (!ProtoRegistrationGuard.TryRegisterOnce<SqlRegistration>(services))
            {
                return;
            }

            // With declared address keys and none provided, the integration is inert and the
            // capability is absent, so gated tests skip; without keys the capability stays
            // unconditional and the factory owns the address (back-compat).
            var addressKeys = DeclaredAddressKeys(services, configure);
            services.AddSingleton(new SqlAddressKeyDeclaration(addressKeys));
            if (addressKeys.Length == 0)
            {
                builder.AddCapability(capability);
            }
            else
            {
                builder.AddCapabilityWhenProvided(capability, addressKeys);
            }

            services.AddScoped(connectionFactory);
            services.AddScoped(services =>
                new ProtoSqlSession(
                    services.GetRequiredService<DbConnection>(),
                    services.GetRequiredService<SqlOptions>()));
            services.TryAddSingleton(provider =>
                ProtoOptionsRegistration.Resolve<SqlOptions>(provider, configure));
            services.AddSingleton<IProtoTestHook>(provider =>
                new SqlConnectionHook(provider.GetRequiredService<SqlOptions>()));
            services.AddSingleton<IProtoRunHook>(provider =>
                new SqlIsolationGuardHook(
                    provider.GetRequiredService<SqlOptions>(),
                    provider.GetServices<ProtoApplicationClients>(),
                    provider.GetServices<ProtoCapabilityDescriptor>().Any(IsStoreCapability)));
        });
    }

    /// <summary>
    /// The address keys the options the run will use declare: a host-registered <see cref="SqlOptions"/>
    /// instance keeps its keys, otherwise the callback's probe decides.
    /// </summary>
    private static string[] DeclaredAddressKeys(
        IServiceCollection services,
        Action<SqlOptions>? configure)
    {
        var hostOptions = services
            .LastOrDefault(descriptor => descriptor.ServiceType == typeof(SqlOptions))?
            .ImplementationInstance as SqlOptions;
        if (hostOptions is not null)
        {
            return hostOptions.AddressKeys.ToArray();
        }

        var declared = new SqlOptions();
        configure?.Invoke(declared);
        return declared.AddressKeys.ToArray();
    }

    private static bool IsStoreCapability(ProtoCapabilityDescriptor capability)
        => string.Equals(capability.Kind, ProtoCapabilityKinds.Store, StringComparison.Ordinal)
           && string.Equals(capability.Name, CapabilityName, StringComparison.Ordinal);
}
