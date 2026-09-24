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

    /// <summary>
    /// Gives every test its own <see cref="DbConnection"/>, opened and owned as a resource. The factory
    /// runs inside the test scope, so it can read a container's connection string from per-test state.
    /// Any access technology can share that connection - Entity Framework Core, Dapper or raw ADO.NET.
    /// A second registration is a no-op: the first call's factory and options win. A host that registered
    /// its own <see cref="SqlOptions"/> keeps them and the rest of the integration composes around them.
    /// </summary>
    public static IProtoHostBuilder AddSql(
        this IProtoHostBuilder builder,
        Func<IServiceProvider, DbConnection> connectionFactory,
        Action<SqlOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(connectionFactory);

        return builder
            .AddCapability(new ProtoCapabilityDescriptor("SQL", ProtoCapabilityKinds.Store, ProtoSqlSession.TraceSource))
            .ConfigureServices(services =>
            {
                // The marker, not the presence of SqlOptions, decides whether SQL already registered: a
                // host that registered its own options keeps them (the options registration is TryAdd)
                // instead of silently disabling the connection, session, hooks and guard.
                if (!ProtoRegistrationGuard.TryRegisterOnce<SqlRegistration>(services))
                {
                    return;
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
                    new SqlIsolationGuardHook(provider.GetRequiredService<SqlOptions>(), provider.GetServices<ProtoApplicationClients>()));
            });
    }
}
