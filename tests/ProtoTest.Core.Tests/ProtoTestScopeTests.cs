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

    [Test]
    public async Task DisposeAsync_WhenDisposedOffFlow_ShouldFailLoudly()
    {
        // A5.3 (Audit 5, A5-12): completion reads the ambient context, so off-flow it would complete
        // nothing. The scope owns its started test and reports the mismatch instead of no-oping.
        await using var host = new ProtoHostBuilder().Build();
        await host.StartAsync();
        var preparation = ProtoTestAdapter.Prepare(TestMethods.Placeholder, host);
        var scope = await ProtoTestScope.StartAsync(preparation, host);
        scope.Result = ProtoTestResult.Passed;

        Exception? failure = null;
        Task disposal;
        using (ExecutionContext.SuppressFlow())
        {
            // A flow that never saw the test: no ambient context reaches the dispose call.
            disposal = Task.Run(async () =>
            {
                try
                {
                    await scope.DisposeAsync();
                }
                catch (InvalidOperationException exception)
                {
                    failure = exception;
                }
            });
        }
        await disposal;

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.Not.Null, "a completion that did nothing is reported");
            Assert.That(failure!.Message, Does.Contain("did not start it"));
            Assert.That(
                host.Trace.Snapshot().Tests.Single().Outcome,
                Is.EqualTo(ProtoTraceOutcome.Unknown),
                "the test never completed, and the scope said so");
        });
    }

    [Test]
    public async Task DisposeAsync_WhenAnotherHostsContextIsActive_ShouldRecordAFindingAndFail()
    {
        // A5.3 (Audit 5, A5-12): a foreign active context would complete the wrong test; the scope
        // surfaces a finding on its own test and reports the mismatch.
        var sink = new CapturingSink();
        var firstBuilder = new ProtoHostBuilder();
        firstBuilder.AddSink(sink);
        await using var firstHost = firstBuilder.Build();
        await using var secondHost = new ProtoHostBuilder().Build();
        await firstHost.StartAsync();
        await secondHost.StartAsync();

        var scope = await ProtoTestScope.StartAsync(
            ProtoTestAdapter.Prepare(TestMethods.Placeholder, firstHost), firstHost);
        scope.Result = ProtoTestResult.Passed;

        // A separate flow starts the second host's test, so the active context there is a foreign one
        // while the scope's own context stays on this flow's stack.
        Exception? failure = null;
        Task disposal;
        using (ExecutionContext.SuppressFlow())
        {
            disposal = Task.Run(async () =>
            {
                await secondHost.StartTestAsync("other test", "00001", TestMethods.Placeholder);
                try
                {
                    await scope.DisposeAsync();
                }
                catch (InvalidOperationException exception)
                {
                    failure = exception;
                }
                await secondHost.CompleteTestAsync(ProtoTestResult.Passed);
            });
        }
        await disposal;

        Assert.That(failure, Is.Not.Null);
        Assert.That(failure!.Message, Does.Contain("another test"));

        await firstHost.StopAsync();

        // The finding is evidence on the scope's own test and reaches the run's report even though
        // that test could not complete.
        var finding = sink.Items.Single(item => item.Category == "Lifecycle");
        Assert.Multiple(() =>
        {
            Assert.That(finding.Status, Is.EqualTo(ProtoReportStatus.Error));
            Assert.That(finding.Message, Does.Contain("another test"));
        });
    }

    private sealed class FailingTeardownHook : IProtoTestHook
    {
        public Task BeforeTestAsync(ProtoExecutionContext context) => Task.CompletedTask;

        public Task AfterTestAsync(ProtoExecutionContext context)
            => throw new InvalidOperationException("teardown failed");
    }
}
