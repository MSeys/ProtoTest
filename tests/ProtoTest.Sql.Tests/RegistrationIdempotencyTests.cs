namespace ProtoTest.Sql.Tests;

using System.Data;
using System.Data.Common;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Sql.EntityFrameworkCore;

[TestFixture]
[NonParallelizable]
public sealed class RegistrationIdempotencyTests
{
    private SqliteKeeper _database = null!;

    [SetUp]
    public void SetUp() => _database = new SqliteKeeper("sqlidempotent");

    [TearDown]
    public void TearDown() => _database.Dispose();

    [Test]
    public async Task AddSql_CalledTwice_ShouldKeepTheFirstRegistrationAndRun()
    {
        var builder = new ProtoHostBuilder();
        IServiceCollection? services = null;
        builder.ConfigureServices(collection => services = collection);
        builder.AddSql(_ => new SqliteConnection(_database.ConnectionString));
        builder.AddSql(_ => throw new InvalidOperationException("The second SQL connection factory must not register."));

        Assert.Multiple(() =>
        {
            Assert.That(services!.Count(descriptor => descriptor.ServiceType == typeof(SqlOptions)), Is.EqualTo(1));
            Assert.That(services!.Count(descriptor => descriptor.ServiceType == typeof(IProtoTestHook)), Is.EqualTo(1));
            Assert.That(services!.Count(descriptor => descriptor.ServiceType == typeof(IProtoRunHook)), Is.EqualTo(1));
            Assert.That(
                services!.Count(descriptor => descriptor.ServiceType == typeof(ProtoCapabilityDescriptor)),
                Is.EqualTo(1));
        });

        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("sql idempotent", TestMethods.Placeholder);

        var connection = Proto.Context.SqlConnection();
        Assert.Multiple(() =>
        {
            Assert.That(connection.State, Is.EqualTo(ConnectionState.Open));
            Assert.That(Proto.Context.SqlTransaction(), Is.Not.Null);
        });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task AddSql_AfterTheHostRegisteredOptions_ShouldComposeAroundThem()
    {
        var hostOptions = new SqlOptions();
        var builder = new ProtoHostBuilder()
            .ConfigureServices(services => services.AddSingleton(hostOptions))
            .AddSql(_ => new SqliteConnection(_database.ConnectionString));

        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("sql composed options", TestMethods.Placeholder);

        Assert.Multiple(() =>
        {
            Assert.That(Proto.Context.Service<SqlOptions>(), Is.SameAs(hostOptions),
                "the host's options are used instead of disabling the integration");
            Assert.That(Proto.Context.SqlConnection().State, Is.EqualTo(ConnectionState.Open),
                "the connection, session, hooks and guard still compose");
        });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task AddEntityFrameworkCore_CalledTwiceForTheSameContext_ShouldKeepTheFirstRegistration()
    {
        var builder = new ProtoHostBuilder();
        IServiceCollection? services = null;
        builder.ConfigureServices(collection => services = collection);
        builder.AddSql(_ => new SqliteConnection(_database.ConnectionString));
        builder.AddEntityFrameworkCore<WidgetDbContext>((provider, options) =>
            options.UseSqlite(provider.GetRequiredService<DbConnection>()));
        builder.AddEntityFrameworkCore<WidgetDbContext>((_, _) =>
            throw new InvalidOperationException("The second context configuration must not register."));

        // One SQL connection hook plus one enlistment hook for the single context.
        Assert.That(services!.Count(descriptor => descriptor.ServiceType == typeof(IProtoTestHook)), Is.EqualTo(2));

        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("ef idempotent", TestMethods.Placeholder);

        var context = Proto.Context.Sql<WidgetDbContext>();
        context.Widgets.Add(new Widget { Name = "gear" });
        await context.SaveChangesAsync();
        Assert.That(await context.Widgets.CountAsync(), Is.EqualTo(1));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public void AddEntityFrameworkCore_WithDifferentContexts_ShouldRegisterBoth()
    {
        var builder = new ProtoHostBuilder();
        IServiceCollection? services = null;
        builder.ConfigureServices(collection => services = collection);
        builder.AddSql(_ => new SqliteConnection(_database.ConnectionString));
        builder.AddEntityFrameworkCore<WidgetDbContext>((provider, options) =>
            options.UseSqlite(provider.GetRequiredService<DbConnection>()));
        builder.AddEntityFrameworkCore<OrderDbContext>((provider, options) =>
            options.UseSqlite(provider.GetRequiredService<DbConnection>()));

        Assert.Multiple(() =>
        {
            // Two different TContexts are different registrations, not duplicates.
            Assert.That(
                services!.Count(descriptor => descriptor.ServiceType == typeof(DbContextOptions<WidgetDbContext>)),
                Is.EqualTo(1));
            Assert.That(
                services!.Count(descriptor => descriptor.ServiceType == typeof(DbContextOptions<OrderDbContext>)),
                Is.EqualTo(1));
            // One SQL connection hook plus one enlistment hook per context.
            Assert.That(services!.Count(descriptor => descriptor.ServiceType == typeof(IProtoTestHook)), Is.EqualTo(3));
        });
    }

    [Test]
    public async Task AddEntityFrameworkCore_WhenTheHostRegisteredTheContext_ShouldStillEnlistIt()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureServices(services => services.AddDbContext<WidgetDbContext>(
            (provider, options) => options.UseSqlite(provider.GetRequiredService<DbConnection>())));
        builder.AddSql(_ => new SqliteConnection(_database.ConnectionString));
        builder.AddEntityFrameworkCore<WidgetDbContext>((_, _) => { });

        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("ef host registered", TestMethods.Placeholder);

        var context = Proto.Context.Sql<WidgetDbContext>();
        Assert.That(context.Database.GetDbConnection(), Is.SameAs(Proto.Context.SqlConnection()),
            "The host-registered context must enlist in the test's connection.");

        context.Widgets.Add(new Widget { Name = "gear" });
        await context.SaveChangesAsync();
        Assert.That(await context.Widgets.CountAsync(), Is.EqualTo(1));

        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.That(SqliteKeeper.CountWidgets(_database.Connection), Is.Zero, "Saving through an enlisted context must roll back with the test.");
        await host.StopAsync();
    }

    [Test]
    public async Task AddEntityFrameworkCore_CalledBeforeTheHostAddDbContext_ShouldStillEnlistAndRollBack()
    {
        var builder = new ProtoHostBuilder();
        builder.AddSql(_ => new SqliteConnection(_database.ConnectionString));
        builder.AddEntityFrameworkCore<WidgetDbContext>((provider, options) =>
            options.UseSqlite(provider.GetRequiredService<DbConnection>()));
        builder.ConfigureServices(services => services.AddDbContext<WidgetDbContext>(
            (provider, options) => options.UseSqlite(provider.GetRequiredService<DbConnection>())));

        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("ef proto-first order", TestMethods.Placeholder);

        var context = Proto.Context.Sql<WidgetDbContext>();
        Assert.That(context.Database.GetDbConnection(), Is.SameAs(Proto.Context.SqlConnection()),
            "The test connection must win in either call order.");

        context.Widgets.Add(new Widget { Name = "gear" });
        await context.SaveChangesAsync();
        Assert.That(await context.Widgets.CountAsync(), Is.EqualTo(1));

        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.That(SqliteKeeper.CountWidgets(_database.Connection), Is.Zero, "Saving through an enlisted context must roll back with the test.");
        await host.StopAsync();
    }

    [Test]
    public async Task AddEntityFrameworkCore_WhenTheHostContextUsesItsOwnConnection_ShouldDiagnoseTheOrder()
    {
        using var hostConnection = new SqliteConnection(_database.ConnectionString);
        hostConnection.Open();
        var builder = new ProtoHostBuilder();
        // Isolation None means no transaction is begun; the connection check must still run, or a
        // host AddDbContext after AddEntityFrameworkCore would silently point at another database.
        builder.AddSql(_ => new SqliteConnection(_database.ConnectionString), sql => sql.Isolation = SqlIsolation.None);
        builder.ConfigureServices(services => services.AddDbContext<WidgetDbContext>(
            (provider, options) => options.UseSqlite(hostConnection)));
        builder.AddEntityFrameworkCore<WidgetDbContext>((_, _) => { });

        await using var host = builder.Build();
        await host.StartAsync();

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await host.StartTestAsync("ef own connection", TestMethods.Placeholder));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("uses its own connection"));
            Assert.That(exception.Message, Does.Contain("AddEntityFrameworkCore"),
                "The diagnostic must point at the registration order.");
        });
        await host.StopAsync();
    }

    [Test]
    public async Task AddEntityFrameworkCore_WhenTheHostRegisteredTheContext_ShouldEnlistOnlyOnce()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureServices(services => services.AddDbContext<WidgetDbContext>(
            (provider, options) => options.UseSqlite(provider.GetRequiredService<DbConnection>())));
        builder.AddSql(_ => new SqliteConnection(_database.ConnectionString));
        builder.AddEntityFrameworkCore<WidgetDbContext>((_, _) => { });
        builder.AddEntityFrameworkCore<WidgetDbContext>((_, _) => { });

        IServiceCollection? services = null;
        builder.ConfigureServices(collection => services = collection);

        Assert.That(services!.Count(descriptor =>
            descriptor.ServiceType == typeof(IProtoTestHook)), Is.EqualTo(2),
            "One SQL connection hook plus one enlistment hook for the context.");
    }


    private static void SampleTest()
    {
    }
}

public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public DbSet<Widget> Widgets => Set<Widget>();
}
