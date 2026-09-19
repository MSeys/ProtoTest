namespace ProtoTest.Sql;

using System.Data.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Sql.Internal;

public static class ProtoHostBuilderExtensions
{
    /// <summary>
    /// Gives every test its own <see cref="DbConnection"/>, opened and owned as a resource. The factory
    /// runs inside the test scope, so it can read a container's connection string from per-test state.
    /// Any access technology can share that connection - Entity Framework Core, Dapper or raw ADO.NET.
    /// A second registration is a no-op: the first call's factory and options win.
    /// </summary>
    public static IProtoHostBuilder AddSql(
        this IProtoHostBuilder builder,
        Func<IServiceProvider, DbConnection> connectionFactory,
        Action<SqlOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(connectionFactory);

        return builder
            .AddCapability(new ProtoCapabilityDescriptor("SQL", ProtoCapabilityKinds.Store, "ProtoTest.Sql"))
            .ConfigureServices(services =>
            {
                if (services.Any(descriptor => descriptor.ServiceType == typeof(SqlOptions)))
                {
                    return;
                }

                services.AddScoped(connectionFactory);
                services.AddScoped(services =>
                    new ProtoSqlSession(services.GetRequiredService<DbConnection>()));
                services.AddSingleton(provider =>
                {
                    var options = new SqlOptions();
                    configure?.Invoke(options);
                    options.BindFromConfiguration(provider.GetRequiredService<IConfiguration>());
                    return options;
                });
                services.AddSingleton<IProtoTestHook>(provider =>
                    new SqlConnectionHook(provider.GetRequiredService<SqlOptions>()));
                services.AddSingleton<IProtoRunHook>(provider =>
                    new SqlIsolationGuardHook(provider.GetRequiredService<SqlOptions>(), provider.GetServices<ProtoApplicationClients>()));
            });
    }
}
