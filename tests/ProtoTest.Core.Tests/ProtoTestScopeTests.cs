namespace ProtoTest.Core.Tests;

using ProtoTest.Core;

[TestFixture]
public sealed class ProtoTestScopeTests
{
    [Test]
    public async Task DisposeAsync_WhenTeardownFails_ShouldNotThrowAndTraceTheFailure()
    {
        var builder = new ProtoHostBuilder();
        builder.AddTestHook<FailingTeardownHook>();
        await using var host = builder.Build();
        await host.StartAsync();
        var preparation = ProtoTestAdapter.Prepare(TestMethods.Placeholder, host);

        var scope = await ProtoTestScope.StartAsync(preparation, host);
        scope.Result = ProtoTestResult.Passed;

        Assert.DoesNotThrowAsync(
            async () => await scope.DisposeAsync(),
            "a teardown failure is a finding, never the test's result");

        var teardown = host.Trace.Snapshot().Tests.Single().Entries
            .Single(entry => entry.Kind == "test.teardown");
        Assert.That(teardown.Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
    }

    [Test]
    public async Task DisposeAsync_WhenCalledTwice_ShouldCompleteOnce()
    {
        await using var host = new ProtoHostBuilder().Build();
        await host.StartAsync();
        var preparation = ProtoTestAdapter.Prepare(TestMethods.Placeholder, host);
        var scope = await ProtoTestScope.StartAsync(preparation, host);
        scope.Result = ProtoTestResult.Passed;

        await scope.DisposeAsync();
        Assert.DoesNotThrowAsync(async () => await scope.DisposeAsync());
        Assert.That(host.Trace.Snapshot().Tests, Has.Count.EqualTo(1));
    }

    private sealed class FailingTeardownHook : IProtoTestHook
    {
        public Task BeforeTestAsync(ProtoExecutionContext context) => Task.CompletedTask;

        public Task AfterTestAsync(ProtoExecutionContext context)
            => throw new InvalidOperationException("teardown failed");
    }
}
