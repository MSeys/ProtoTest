namespace ProtoTest.Sql.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Sql.EntityFrameworkCore;
using System.Data.Common;
using System.Reflection;

[TestFixture]
[NonParallelizable]
public sealed class SqlIsolationTests
{
    // Pooling is off so the keeper connection alone decides when the in-memory database disappears;
    // a pooled connection would outlive the test and leak both data and locks.
    private const string ConnectionString = "Data Source=file:sqltests;Mode=Memory;Cache=Shared;Pooling=False";

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
    public async Task RawCommands_ShouldRollBackWithTransactionPerTest()
    {
        // Arrange
        await using var host = CreateHost();
        await host.StartAsync();
        await host.StartTestAsync("raw rollback", TestMethod());

        // Act
        var connection = Proto.Context.SqlConnection();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO Widgets (Name) VALUES ('gear');";
            await command.ExecuteNonQueryAsync();
        }

        // Assert
        Assert.That(CountWidgets(connection), Is.EqualTo(1));

        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.That(CountWidgets(_keeper), Is.Zero);
        await host.StopAsync();
    }

    [Test]
    public async Task EntityFrameworkCore_ShouldShareTheConnectionAndRollBack()
    {
        // Arrange
        await using var host = CreateHost();
        await host.StartAsync();
        await host.StartTestAsync("ef rollback", TestMethod());

        // Act
        var context = Proto.Context.Sql<WidgetDbContext>();
        context.Widgets.Add(new Widget { Name = "gear" });
        await context.SaveChangesAsync();

        // Assert
        Assert.That(await context.Widgets.CountAsync(), Is.EqualTo(1));

        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.That(CountWidgets(_keeper), Is.Zero);
        await host.StopAsync();
    }

    [Test]
    public async Task Sql_ShouldOwnTheConnectionAsAResource()
    {
        // Arrange
        await using var host = CreateHost();
        await host.StartAsync();
        await host.StartTestAsync("owned connection", TestMethod());

        // Act
        var owned = Proto.Context.Resources.Single(resource => resource.Kind == "database");

        // Assert
        Assert.That(owned.State, Is.EqualTo(ProtoResourceState.Registered));

        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var release = host.Trace.Snapshot().Tests.Single().Entries
            .Single(entry => entry.Kind == "resource.release");
        Assert.Multiple(() =>
        {
            Assert.That(release.Attributes["resource.kind"], Is.EqualTo("database"));
            Assert.That(release.Outcome, Is.EqualTo(ProtoTraceOutcome.Succeeded));
        });
        await host.StopAsync();
    }

    [Test]
    public async Task IsolationNone_ShouldPersistWrites()
    {
        // Arrange
        await using var host = CreateHost(SqlIsolation.None);
        await host.StartAsync();
        await host.StartTestAsync("no isolation", TestMethod());

        // Act
        var connection = Proto.Context.SqlConnection();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO Widgets (Name) VALUES ('gear');";
            await command.ExecuteNonQueryAsync();
        }

        // Assert
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.That(CountWidgets(_keeper), Is.EqualTo(1));
        await host.StopAsync();
    }

    private static ProtoHost CreateHost(SqlIsolation isolation = SqlIsolation.TransactionPerTest)
        => new ProtoHostBuilder()
            .AddSql(_ => new SqliteConnection(ConnectionString), sql => sql.Isolation = isolation)
            .AddEntityFrameworkCore<WidgetDbContext>((services, options) =>
                options.UseSqlite(services.GetRequiredService<DbConnection>()))
            .Build();

    private static MethodInfo TestMethod()
        => typeof(SqlIsolationTests).GetMethod(nameof(SampleTest), BindingFlags.NonPublic | BindingFlags.Static)!;

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

public sealed class WidgetDbContext(DbContextOptions<WidgetDbContext> options) : DbContext(options)
{
    public DbSet<Widget> Widgets => Set<Widget>();
}

public sealed class Widget
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}
