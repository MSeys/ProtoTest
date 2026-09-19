namespace ProtoTest.Sql.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Sql.EntityFrameworkCore;
using System.Data;
using System.Data.Common;
using System.Reflection;

[TestFixture]
[NonParallelizable]
public sealed class RegistrationIdempotencyTests
{
    // Keeping the in-memory database alive through a keeper connection keeps the schema available to
    // the scoped connection the test opens and the scoped one the context uses.
    private const string ConnectionString = "Data Source=file:sqlidempotent;Mode=Memory;Cache=Shared;Pooling=False";

    private SqliteConnection _keeper = null!;

    [SetUp]
    public void SetUp()
    {
        _keeper = new SqliteConnection(ConnectionString);
        _keeper.Open();
        Execute(_keeper, "CREATE TABLE IF NOT EXISTS Widgets (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL);");
    }

    [TearDown]
    public void TearDown() => _keeper.Dispose();

    [Test]
    public async Task AddSql_CalledTwice_ShouldKeepTheFirstRegistrationAndRun()
    {
        var builder = new ProtoHostBuilder();
        IServiceCollection? services = null;
        builder.ConfigureServices(collection => services = collection);
        builder.AddSql(_ => new SqliteConnection(ConnectionString));
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
        await host.StartTestAsync("sql idempotent", TestMethod());

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
    public async Task AddEntityFrameworkCore_CalledTwiceForTheSameContext_ShouldKeepTheFirstRegistration()
    {
        var builder = new ProtoHostBuilder();
        IServiceCollection? services = null;
        builder.ConfigureServices(collection => services = collection);
        builder.AddSql(_ => new SqliteConnection(ConnectionString));
        builder.AddEntityFrameworkCore<WidgetDbContext>((provider, options) =>
            options.UseSqlite(provider.GetRequiredService<DbConnection>()));
        builder.AddEntityFrameworkCore<WidgetDbContext>((_, _) =>
            throw new InvalidOperationException("The second context configuration must not register."));

        // One SQL connection hook plus one enlistment hook for the single context.
        Assert.That(services!.Count(descriptor => descriptor.ServiceType == typeof(IProtoTestHook)), Is.EqualTo(2));

        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("ef idempotent", TestMethod());

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
        builder.AddSql(_ => new SqliteConnection(ConnectionString));
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
        builder.AddSql(_ => new SqliteConnection(ConnectionString));
        builder.AddEntityFrameworkCore<WidgetDbContext>((_, _) => { });

        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("ef host registered", TestMethod());

        var context = Proto.Context.Sql<WidgetDbContext>();
        Assert.That(context.Database.GetDbConnection(), Is.SameAs(Proto.Context.SqlConnection()),
            "The host-registered context must enlist in the test's connection.");

        context.Widgets.Add(new Widget { Name = "gear" });
        await context.SaveChangesAsync();
        Assert.That(await context.Widgets.CountAsync(), Is.EqualTo(1));

        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.That(CountWidgets(_keeper), Is.Zero, "Saving through an enlisted context must roll back with the test.");
        await host.StopAsync();
    }

    [Test]
    public async Task AddEntityFrameworkCore_CalledBeforeTheHostAddDbContext_ShouldStillEnlistAndRollBack()
    {
        var builder = new ProtoHostBuilder();
        builder.AddSql(_ => new SqliteConnection(ConnectionString));
        builder.AddEntityFrameworkCore<WidgetDbContext>((provider, options) =>
            options.UseSqlite(provider.GetRequiredService<DbConnection>()));
        builder.ConfigureServices(services => services.AddDbContext<WidgetDbContext>(
            (provider, options) => options.UseSqlite(provider.GetRequiredService<DbConnection>())));

        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("ef proto-first order", TestMethod());

        var context = Proto.Context.Sql<WidgetDbContext>();
        Assert.That(context.Database.GetDbConnection(), Is.SameAs(Proto.Context.SqlConnection()),
            "The test connection must win in either call order.");

        context.Widgets.Add(new Widget { Name = "gear" });
        await context.SaveChangesAsync();
        Assert.That(await context.Widgets.CountAsync(), Is.EqualTo(1));

        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.That(CountWidgets(_keeper), Is.Zero, "Saving through an enlisted context must roll back with the test.");
        await host.StopAsync();
    }

    [Test]
    public async Task AddEntityFrameworkCore_WhenTheHostContextUsesItsOwnConnection_ShouldDiagnoseTheOrder()
    {
        using var hostConnection = new SqliteConnection(ConnectionString);
        hostConnection.Open();
        var builder = new ProtoHostBuilder();
        // Isolation None means no transaction is begun; the connection check must still run, or a
        // host AddDbContext after AddEntityFrameworkCore would silently point at another database.
        builder.AddSql(_ => new SqliteConnection(ConnectionString), sql => sql.Isolation = SqlIsolation.None);
        builder.ConfigureServices(services => services.AddDbContext<WidgetDbContext>(
            (provider, options) => options.UseSqlite(hostConnection)));
        builder.AddEntityFrameworkCore<WidgetDbContext>((_, _) => { });

        await using var host = builder.Build();
        await host.StartAsync();

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await host.StartTestAsync("ef own connection", TestMethod()));

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
        builder.AddSql(_ => new SqliteConnection(ConnectionString));
        builder.AddEntityFrameworkCore<WidgetDbContext>((_, _) => { });
        builder.AddEntityFrameworkCore<WidgetDbContext>((_, _) => { });

        IServiceCollection? services = null;
        builder.ConfigureServices(collection => services = collection);

        Assert.That(services!.Count(descriptor =>
            descriptor.ServiceType == typeof(IProtoTestHook)), Is.EqualTo(2),
            "One SQL connection hook plus one enlistment hook for the context.");
    }

    private static MethodInfo TestMethod()
        => typeof(RegistrationIdempotencyTests).GetMethod(
            nameof(SampleTest), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static void SampleTest()
    {
    }

    private static void Execute(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static int CountWidgets(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Widgets;";
        return Convert.ToInt32(command.ExecuteScalar());
    }
}

public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public DbSet<Widget> Widgets => Set<Widget>();
}
