namespace ProtoTest.Sql;

using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Sql.Internal;

public static class ProtoHostBuilderExtensions
{
    /// <summary>
    /// Gives every test its own <see cref="DbConnection"/>, opened and owned as a resource. The factory
    /// runs inside the test scope, so it can read a container's connection string from per-test state.
    /// Any access technology can share that connection - Entity Framework Core, Dapper or raw ADO.NET.
    /// </summary>
    public static IProtoHostBuilder AddSql(
        this IProtoHostBuilder builder,
        Func<IServiceProvider, DbConnection> connectionFactory,
        Action<ProtoSqlOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(connectionFactory);
        var options = new ProtoSqlOptions();
        configure?.Invoke(options);

        return builder
            .AddCapability(new ProtoCapabilityDescriptor("SQL", ProtoCapabilityKinds.Store, "ProtoTest.Sql"))
            .ConfigureServices(services =>
            {
            services.AddScoped(connectionFactory);
            services.AddScoped(services =>
                new ProtoSqlSession(services.GetRequiredService<DbConnection>()));
            services.AddSingleton(options);
            services.AddSingleton<IProtoTestHook>(new SqlConnectionHook(options));
            services.AddSingleton<IProtoRunHook>(provider =>
                new SqlIsolationGuardHook(options, provider.GetServices<ProtoApplicationClients>()));
        });
    }
}
