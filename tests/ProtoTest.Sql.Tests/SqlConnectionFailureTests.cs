namespace ProtoTest.Sql.Tests;

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


    private static void SampleTest()
    {
    }
}
