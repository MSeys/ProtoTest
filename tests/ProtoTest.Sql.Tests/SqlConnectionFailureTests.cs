namespace ProtoTest.Sql.Tests;

using System.Diagnostics;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Sql.EntityFrameworkCore;

[TestFixture]
[NonParallelizable]
public sealed class SqlConnectionFailureTests
{
    [Test]
    public async Task FailingOpen_ShouldStillDisposeTheConnection()
    {
        var connection = new FailingDbConnection { FailOpen = true };
        await using var host = new ProtoHostBuilder().AddSql(_ => connection).Build();
        await host.StartAsync();

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await host.StartTestAsync("failing open", TestMethods.Placeholder));

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
        var connection = new FailingDbConnection { FailBegin = true };
        await using var host = new ProtoHostBuilder().AddSql(_ => connection).Build();
        await host.StartAsync();

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await host.StartTestAsync("failing begin", TestMethods.Placeholder));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("begin failed"));
            Assert.That(connection.IsDisposed, Is.True,
                "A connection whose transaction failed to start must still be released during teardown.");
        });
        await host.StopAsync();
    }

    [Test]
    public async Task CancelledSetup_ShouldAbortAStalledConnectionOpen()
    {
        var connection = new FailingDbConnection { StallOpen = true };
        await using var host = new ProtoHostBuilder().AddSql(_ => connection).Build();
        await host.StartAsync();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var stopwatch = Stopwatch.StartNew();

        var exception = Assert.CatchAsync<OperationCanceledException>(
            async () => await host.StartTestAsync("cancelled open", TestMethods.Placeholder, cancellation.Token));
        stopwatch.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(3)),
                "the stalled open ends when the token fires, not at the provider's connect timeout or the test double's bound");
            Assert.That(connection.IsDisposed, Is.True,
                "a connection whose open was cancelled is still owned and must be released during teardown");
            Assert.That(host.Trace.Snapshot().Tests.Single().Entries,
                Has.Some.Matches<ProtoTraceEntry>(entry =>
                    entry.Kind == "sql.connection.open" && entry.Outcome == ProtoTraceOutcome.Cancelled),
                "the cancelled open is recorded as a cancelled operation");
            Assert.That(exception, Is.Not.Null);
        });
        await host.StopAsync();
    }

    [Test]
    public async Task CancelledScopeStart_ShouldAbortAStalledConnectionOpen()
    {
        // The SQL entry point for every runner adapter is ProtoTestScope.StartAsync: the token must
        // reach the connection open through it, not only through a hand-written StartTestAsync call.
        var connection = new FailingDbConnection { StallOpen = true };
        await using var host = new ProtoHostBuilder().AddSql(_ => connection).Build();
        await host.StartAsync();
        var preparation = ProtoTestAdapter.Prepare(TestMethods.Placeholder, host);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var stopwatch = Stopwatch.StartNew();

        Assert.CatchAsync<OperationCanceledException>(
            async () => await ProtoTestScope.StartAsync(preparation, host, null, cancellation.Token));
        stopwatch.Stop();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(3)),
                "the stalled open ends when the adapter's token fires");
            Assert.That(connection.IsDisposed, Is.True,
                "a connection whose open was cancelled is still owned and must be released during teardown");
            Assert.That(host.Trace.Snapshot().Tests.Single().Entries,
                Has.Some.Matches<ProtoTraceEntry>(entry =>
                    entry.Kind == "sql.connection.open" && entry.Outcome == ProtoTraceOutcome.Cancelled),
                "the cancelled open is recorded as a cancelled operation");
        }
        await host.StopAsync();
    }

    [Test]
    public async Task CancelledSetup_ShouldAbortAStalledTransactionBegin()
    {
        var connection = new FailingDbConnection { StallBegin = true };
        await using var host = new ProtoHostBuilder().AddSql(_ => connection).Build();
        await host.StartAsync();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var stopwatch = Stopwatch.StartNew();

        var exception = Assert.CatchAsync<OperationCanceledException>(
            async () => await host.StartTestAsync("cancelled begin", TestMethods.Placeholder, cancellation.Token));
        stopwatch.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(3)),
                "the stalled transaction begin ends when the token fires");
            Assert.That(connection.IsDisposed, Is.True,
                "the cancelled begin still releases the connection it owns");
            Assert.That(host.Trace.Snapshot().Tests.Single().Entries,
                Has.Some.Matches<ProtoTraceEntry>(entry =>
                    entry.Kind == "sql.transaction.begin" && entry.Outcome == ProtoTraceOutcome.Cancelled),
                "the cancelled begin is recorded as a cancelled operation");
            Assert.That(exception, Is.Not.Null);
        });
        await host.StopAsync();
    }


    private static void SampleTest()
    {
    }
}
