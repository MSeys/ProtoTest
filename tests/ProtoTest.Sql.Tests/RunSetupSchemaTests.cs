namespace ProtoTest.Sql.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ProtoTest.Core;
using ProtoTest.Sql.EntityFrameworkCore;
using ProtoTest.Sql.Testcontainers;

/// <summary>
/// Audit 5 A5.7b: the run-owned schema recipe. The schema step is registered after the PostgreSQL
/// container, reads the connection string the container published, and creates the schema once for the
/// run. The trial's observed failure (postgres-ef/report.md:72-81) was DDL inside a test transaction
/// being rolled back with it; the second test lifecycle proves the run-created table survives the
/// per-test rollback instead.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class RunSetupSchemaTests
{
    private const string AddressKey = "ConnectionStrings:Orders";

    // One container per fixture, started lazily: a machine without a container runtime skips with the
    // reason instead of failing the suite.
    private static readonly Lazy<(PostgresDatabase? Database, string? Error)> Container = new(
        static () =>
        {
            var result = PostgresDatabase.TryStart();
            return (result.Resource, result.Error);
        },
        LazyThreadSafetyMode.ExecutionAndPublication);

    [OneTimeTearDown]
    public static async Task StopContainer()
    {
        if (Container.IsValueCreated && Container.Value.Database is { } database)
        {
            await database.DisposeAsync();
        }
    }

    [Test]
    public async Task AddRunSetup_ShouldCreateTheSchemaOnceAndSurviveThePerTestRollback()
    {
        var database = RequireDatabase();
        var setups = 0;
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddInfrastructure(database, AddressKey);
        builder.AddSql(
            provider => new NpgsqlConnection(ResolveConnectionString(
                provider.GetRequiredService<ProtoInfrastructureSettings>(),
                provider.GetRequiredService<IConfiguration>())),
            sql => sql.AddressKeys.Add(AddressKey));
        builder.AddEntityFrameworkCore<OrdersDbContext>((services, options) =>
            options.UseNpgsql(services.GetRequiredService<System.Data.Common.DbConnection>()));
        builder.AddRunSetup("database schema", async setup =>
        {
            setups++;
            // Its own connection, outside every test: the step runs before any per-test transaction
            // exists, so the DDL it sends is not rolled back with a test.
            var options = new DbContextOptionsBuilder<OrdersDbContext>()
                .UseNpgsql(ResolveConnectionString(setup.Settings, setup.Configuration))
                .Options;
            await using var schema = new OrdersDbContext(options);
            await schema.Database.EnsureCreatedAsync(setup.CancellationToken);
        });
        await using var host = builder.Build();
        await host.StartAsync();

        // Test 1: the schema exists and the row the test writes is visible inside its transaction.
        await host.StartTestAsync("schema visible", "00001", TestMethods.Placeholder);
        var first = Proto.Context.Sql<OrdersDbContext>();
        first.Orders.Add(new Order { Reference = "ORD-1" });
        await first.SaveChangesAsync();
        var visible = await first.Orders.CountAsync();
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        // Test 2: the run-created table is still there; the first test's row is gone with its rollback.
        await host.StartTestAsync("schema survives", "00002", TestMethods.Placeholder);
        var remaining = await Proto.Context.Sql<OrdersDbContext>().Orders.ToListAsync();
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(setups, Is.EqualTo(1), "the schema step runs once for the run");
            Assert.That(visible, Is.EqualTo(1), "the test sees its own write inside the transaction");
            Assert.That(remaining, Is.Empty, "the per-test transaction rolled the row back");
        });
    }

    private static PostgresDatabase RequireDatabase()
    {
        if (Container.Value.Database is { } database)
        {
            return database;
        }

        Assert.Ignore(
            "No container runtime is available for the PostgreSQL fixture. " + Container.Value.Error);
        throw new InvalidOperationException("unreachable");
    }

    /// <summary>The published-settings-first precedence every address reader follows.</summary>
    private static string ResolveConnectionString(
        ProtoInfrastructureSettings settings,
        IConfiguration configuration)
    {
        if (settings.Values.TryGetValue(AddressKey, out var published))
        {
            return published;
        }

        return configuration[AddressKey]
            ?? throw new InvalidOperationException(
                $"No connection string under '{AddressKey}'. Register the container or configure the key.");
    }
}

public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
}

public sealed class Order
{
    public int Id { get; set; }

    public string Reference { get; set; } = string.Empty;
}
