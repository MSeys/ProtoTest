namespace ProtoTest.Sql.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Sql.EntityFrameworkCore;
using System.Reflection;

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
        var connection = new FailingDbConnection { FailBegin = true };
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
}
