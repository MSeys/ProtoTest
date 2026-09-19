namespace ProtoTest.Sql.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Sql.EntityFrameworkCore;
using System.Data;
using System.Data.Common;
using System.Reflection;

[TestFixture]
[NonParallelizable]
public sealed class SqlConnectionFailureTests
{
    [Test]
    public async Task FailingOpen_ShouldStillDisposeTheConnection()
    {
        var connection = new FailingConnection { FailOpen = true };
        await using var host = new ProtoHostBuilder().AddSql(_ => connection).Build();
        await host.StartAsync();

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await host.StartTestAsync("failing open", TestMethod()));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("open failed"));
            Assert.That(connection.IsDisposed, Is.True,
                "A connection that failed to open is still owned and must be released during teardown.");
        });
        await host.StopAsync();
    }

    [Test]
    public async Task FailingBegin_ShouldStillDisposeTheConnection()
    {
        var connection = new FailingConnection { FailBegin = true };
        await using var host = new ProtoHostBuilder().AddSql(_ => connection).Build();
        await host.StartAsync();

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await host.StartTestAsync("failing begin", TestMethod()));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("begin failed"));
            Assert.That(connection.IsDisposed, Is.True,
                "A connection whose transaction failed to start must still be released during teardown.");
        });
        await host.StopAsync();
    }

    private static MethodInfo TestMethod()
        => typeof(SqlConnectionFailureTests).GetMethod(
            nameof(SampleTest), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static void SampleTest()
    {
    }

    private sealed class FailingConnection : DbConnection
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;

        public bool FailOpen { get; init; }

        public bool FailBegin { get; init; }

        public bool IsDisposed { get; private set; }

        public override string Database => "fake";

        public override string DataSource => "fake";

        public override string ServerVersion => "1";

        public override ConnectionState State => _state;

        private ConnectionState _state = ConnectionState.Closed;

        public override void ChangeDatabase(string databaseName)
        {
        }

        public override void Close() => _state = ConnectionState.Closed;

        public override void Open() => _state = ConnectionState.Open;

        public override Task OpenAsync(CancellationToken cancellationToken)
        {
            if (FailOpen)
            {
                throw new InvalidOperationException("open failed");
            }

            _state = ConnectionState.Open;
            return Task.CompletedTask;
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
            => FailBegin
                ? throw new InvalidOperationException("begin failed")
                : new FakeTransaction(this, isolationLevel);

        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            _state = ConnectionState.Closed;
            base.Dispose(disposing);
        }
    }

    private sealed class FakeTransaction(DbConnection connection, IsolationLevel isolationLevel) : DbTransaction
    {
        public override IsolationLevel IsolationLevel { get; } = isolationLevel;

        protected override DbConnection DbConnection { get; } = connection;

        public override void Commit()
        {
        }

        public override void Rollback()
        {
        }
    }
}
