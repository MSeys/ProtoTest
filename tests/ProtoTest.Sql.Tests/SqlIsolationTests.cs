namespace ProtoTest.Sql.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Sql.EntityFrameworkCore;
using System.Data;
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
    public async Task RawCommands_ShouldRollBackWithTransaction()
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
    public async Task Sql_ShouldTraceOpenBeginAndRollback()
    {
        // Arrange
        await using var host = CreateHost();
        await host.StartAsync();
        await host.StartTestAsync("sql traced lifecycle", TestMethod());

        // Act
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        // Assert
        var entries = host.Trace.Snapshot().Tests.Single().Entries;
        var open = entries.Single(entry => entry.Kind == "sql.connection.open");
        var begin = entries.Single(entry => entry.Kind == "sql.transaction.begin");
        var rollback = entries.Single(entry => entry.Kind == "sql.transaction.rollback");
        var release = entries.Single(entry => entry.Kind == "resource.release");
        Assert.Multiple(() =>
        {
            Assert.That(open.Phase, Is.EqualTo(ProtoTracePhase.Setup));
            Assert.That(open.Source, Is.EqualTo("ProtoTest.Sql"));
            Assert.That(open.Outcome, Is.EqualTo(ProtoTraceOutcome.Succeeded));
            Assert.That(open.Attributes["sql.connection.type"], Is.EqualTo(typeof(SqliteConnection).FullName));

            Assert.That(begin.Phase, Is.EqualTo(ProtoTracePhase.Setup));
            Assert.That(begin.ParentId, Is.EqualTo(open.Id));
            Assert.That(begin.Outcome, Is.EqualTo(ProtoTraceOutcome.Succeeded));
            Assert.That(begin.Attributes["sql.isolation"], Is.EqualTo(nameof(SqlIsolation.Transaction)));

            Assert.That(rollback.Phase, Is.EqualTo(ProtoTracePhase.Teardown));
            Assert.That(rollback.ParentId, Is.EqualTo(release.Id));
            Assert.That(rollback.Outcome, Is.EqualTo(ProtoTraceOutcome.Succeeded));
        });

        await host.StopAsync();
    }

    [Test]
    public async Task EntityFrameworkCore_ShouldTraceEnlistment()
    {
        // Arrange
        await using var host = CreateHost();
        await host.StartAsync();
        await host.StartTestAsync("ef traced enlistment", TestMethod());

        // Act
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        // Assert
        var enlist = host.Trace.Snapshot().Tests.Single().Entries
            .Single(entry => entry.Kind == "sql.enlist");
        Assert.Multiple(() =>
        {
            Assert.That(enlist.Phase, Is.EqualTo(ProtoTracePhase.Setup));
            Assert.That(enlist.Source, Is.EqualTo("ProtoTest.Sql.EntityFrameworkCore"));
            Assert.That(enlist.Outcome, Is.EqualTo(ProtoTraceOutcome.Succeeded));
            Assert.That(enlist.Attributes["db.context"], Is.EqualTo(typeof(WidgetDbContext).FullName));
        });

        await host.StopAsync();
    }

    [Test]
    public async Task IsolationNone_ShouldTraceTheOpenWithoutATransaction()
    {
        // Arrange
        await using var host = CreateHost(SqlIsolation.None);
        await host.StartAsync();
        await host.StartTestAsync("no transaction trace", TestMethod());

        // Act
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        // Assert
        var kinds = host.Trace.Snapshot().Tests.Single().Entries.Select(entry => entry.Kind).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(kinds, Does.Contain("sql.connection.open"));
            Assert.That(kinds, Does.Not.Contain("sql.transaction.begin"));
            Assert.That(kinds, Does.Not.Contain("sql.transaction.rollback"));
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

    [Test]
    public async Task Transaction_ShouldFailAtRunStartWhenAnApplicationIsNotDeclaredAsSharing()
    {
        // Arrange
        await using var host = new ProtoHostBuilder()
            .AddApplication("TestApp", _ => { })
            .AddSql(_ => new SqliteConnection(ConnectionString))
            .Build();

        // Act
        var exception = Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());

        // Assert
        Assert.That(exception!.Message, Does.Contain("ShareConnectionWith"));
    }

    [Test]
    public async Task Transaction_ShouldStartWhenEveryApplicationIsDeclaredAsSharing()
    {
        // Arrange
        await using var host = new ProtoHostBuilder()
            .AddApplication("TestApp", _ => { })
            .AddSql(_ => new SqliteConnection(ConnectionString), sql => sql.ShareConnectionWith("TestApp"))
            .Build();

        // Act
        await host.StartAsync();

        // Assert
        Assert.That(host.Trace.Snapshot().RunId, Is.Not.Empty);

        await host.StopAsync();
    }

    [Test]
    public async Task SqlSession_ShouldOwnTheConnectionAndExposeTheTransaction()
    {
        // Arrange
        await using var host = CreateHost();
        await host.StartAsync();
        await host.StartTestAsync("session accessors", TestMethod());

        // Act
        var session = Proto.Context.SqlSession();
        var connection = Proto.Context.SqlConnection();
        var transaction = Proto.Context.SqlTransaction();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(session.Connection, Is.SameAs(connection));
            Assert.That(transaction, Is.Not.Null);
            Assert.That(session.Transaction, Is.SameAs(transaction));
            Assert.That(transaction!.Connection, Is.SameAs(connection));
        });

        await host.CompleteTestAsync(ProtoTestResult.Passed);

        // The connection is the run-owned one: the test's release disposed it.
        Assert.That(connection.State, Is.EqualTo(ConnectionState.Closed));
        await host.StopAsync();
    }

    [Test]
    public async Task SqlTransaction_ShouldBeNullUnderIsolationNone()
    {
        // Arrange
        await using var host = CreateHost(SqlIsolation.None);
        await host.StartAsync();
        await host.StartTestAsync("no transaction", TestMethod());

        // Act and assert
        Assert.Multiple(() =>
        {
            Assert.That(Proto.Context.SqlTransaction(), Is.Null);
            Assert.That(Proto.Context.SqlSession().Transaction, Is.Null);
        });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task Configuration_ShouldBindIsolationAndSharedApplications()
    {
        // Arrange
        await using var host = new ProtoHostBuilder()
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ProtoTest:Sql:Isolation"] = "None",
                    ["ProtoTest:Sql:SharedWithApplications:0"] = "Api"
                }))
            .AddApplication("Api", _ => { })
            .AddSql(_ => new SqliteConnection(ConnectionString))
            .Build();

        // Act: the isolation guard passes because the named application is declared as sharing.
        await host.StartAsync();
        await host.StartTestAsync("configured sql", TestMethod());

        var options = Proto.Context.Service<SqlOptions>();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(options.Isolation, Is.EqualTo(SqlIsolation.None));
            Assert.That(options.SharedWith, Is.EquivalentTo(new[] { "Api" }));
            Assert.That(options.SharesConnectionWith("Api"), Is.True);
        });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task RollbackFailure_ShouldStillDisposeTheTransactionAndConnection()
    {
        var connection = new FailingRollbackConnection();
        await using var host = new ProtoHostBuilder().AddSql(_ => connection).Build();
        await host.StartAsync();
        await host.StartTestAsync("rollback failure", TestMethod());

        var exception = Assert.ThrowsAsync<AggregateException>(
            async () => await host.CompleteTestAsync(ProtoTestResult.Passed));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.InnerExceptions.Any(item => item.Message.Contains("rollback failed")), Is.True);
            Assert.That(connection.Transaction, Is.Not.Null);
            Assert.That(connection.Transaction!.Disposed, Is.True, "The transaction must be disposed even when rollback fails.");
            Assert.That(connection.IsDisposed, Is.True, "The connection must be disposed even when rollback fails.");
        });
        await host.StopAsync();
    }

    private static ProtoHost CreateHost(SqlIsolation isolation = SqlIsolation.Transaction)
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

internal sealed class FailingRollbackConnection : DbConnection
{
    private ConnectionState _state = ConnectionState.Closed;

    public FailingRollbackTransaction? Transaction { get; private set; }

    public bool IsDisposed { get; private set; }

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string ConnectionString { get; set; } = string.Empty;

    public override string Database => "fake";

    public override string DataSource => "fake";

    public override string ServerVersion => "1";

    public override ConnectionState State => _state;

    public override void ChangeDatabase(string databaseName)
    {
    }

    public override void Close() => _state = ConnectionState.Closed;

    public override void Open() => _state = ConnectionState.Open;

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        Transaction = new FailingRollbackTransaction(this);
        return Transaction;
    }

    protected override DbCommand CreateDbCommand() => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
        base.Dispose(disposing);
    }
}

internal sealed class FailingRollbackTransaction(DbConnection connection) : DbTransaction
{
    public bool Disposed { get; private set; }

    public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

    protected override DbConnection DbConnection { get; } = connection;

    public override void Rollback() => throw new InvalidOperationException("rollback failed");

    public override void Commit()
    {
    }

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }
}
