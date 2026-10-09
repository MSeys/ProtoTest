namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

[TestFixture]
public sealed class ProtoTestScopeTests
{
    [Test]
    public async Task DisposeAsync_WhenTeardownFails_ShouldFailTheTestAndNameTheCleanup()
    {
        var builder = new ProtoHostBuilder();
        builder.AddTestHook<FailingTeardownHook>();
        await using var host = builder.Build();
        await host.StartAsync();
        var preparation = ProtoTestAdapter.Prepare(TestMethods.Placeholder, host);

        var scope = await ProtoTestScope.StartAsync(preparation, host);
        scope.Result = ProtoTestResult.Passed;

        var failure = Assert.ThrowsAsync<ProtoCleanupException>(async () => await scope.DisposeAsync());

        var test = host.Trace.Snapshot().Tests.Single();
        var teardown = test.Entries.Single(entry => entry.Kind == "test.teardown");
        Assert.Multiple(() =>
        {
            Assert.That(failure!.BodyPassed, Is.True);
            Assert.That(
                failure.Message,
                Is.EqualTo("The test body passed, but cleanup failed: InvalidOperationException: teardown failed."));
            Assert.That(failure.InnerException, Is.TypeOf<InvalidOperationException>());
            Assert.That(test.Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
            Assert.That(test.Error!.Message, Is.EqualTo(failure.Message));
            Assert.That(teardown.Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
        });
    }

    [Test]
    public async Task DisposeAsync_WhenCleanupFailuresAreReported_ShouldKeepThePassingResult()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureCleanup(options => options.CleanupFailures = ProtoCleanupFailureMode.Report);
        builder.AddTestHook<FailingTeardownHook>();
        await using var host = builder.Build();
        await host.StartAsync();
        var preparation = ProtoTestAdapter.Prepare(TestMethods.Placeholder, host);
        var scope = await ProtoTestScope.StartAsync(preparation, host);
        scope.Result = ProtoTestResult.Passed;

        Assert.DoesNotThrowAsync(async () => await scope.DisposeAsync());

        var test = host.Trace.Snapshot().Tests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(test.Outcome, Is.EqualTo(ProtoTraceOutcome.Partial));
            Assert.That(test.Error, Is.Null);
            Assert.That(
                test.Record!.Findings,
                Has.Some.Matches<ProtoTraceFindingRecord>(finding =>
                    finding.Category == "Teardown" && finding.Message.Contains("teardown failed")));
        });
    }

    [Test]
    public async Task DisposeAsync_WhenConfigurationReportsCleanupFailures_ShouldKeepThePassingResult()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:CleanupFailures"] = "Report"
            }));
        builder.AddTestHook<FailingTeardownHook>();
        await using var host = builder.Build();
        await host.StartAsync();
        var preparation = ProtoTestAdapter.Prepare(TestMethods.Placeholder, host);
        var scope = await ProtoTestScope.StartAsync(preparation, host);
        scope.Result = ProtoTestResult.Passed;

        Assert.DoesNotThrowAsync(async () => await scope.DisposeAsync());

        Assert.That(
            host.Trace.Snapshot().Tests.Single().Outcome,
            Is.EqualTo(ProtoTraceOutcome.Partial));
    }

    [Test]
    public async Task DisposeAsync_WhenTheBodyFailed_ShouldKeepThatFailureAndAttachCleanup()
    {
        var builder = new ProtoHostBuilder();
        builder.AddTestHook<FailingTeardownHook>();
        await using var host = builder.Build();
        await host.StartAsync();
        var preparation = ProtoTestAdapter.Prepare(TestMethods.Placeholder, host);
        var scope = await ProtoTestScope.StartAsync(preparation, host);
        var assertion = new InvalidOperationException("the assertion failed");
        scope.Result = ProtoTestResult.Failed(assertion);

        var failure = Assert.ThrowsAsync<ProtoCleanupException>(async () => await scope.DisposeAsync());

        var test = host.Trace.Snapshot().Tests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(failure!.BodyPassed, Is.False);
            Assert.That(failure.Message, Does.StartWith("the assertion failed"));
            Assert.That(failure.Message, Does.Contain("Cleanup also failed: InvalidOperationException: teardown failed."));
            Assert.That(failure.InnerException, Is.SameAs(assertion));
            Assert.That(test.Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
            Assert.That(test.Error!.Message, Is.EqualTo("the assertion failed"));
        });
    }

    [Test]
    public async Task DisposeAsync_WhenSeveralCleanupsFail_ShouldNameTheFirstAndCountTheRest()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IProtoTestHook>(new FailingTeardownHook("alpha") { Order = 10 });
            services.AddSingleton<IProtoTestHook>(new FailingTeardownHook("beta") { Order = 20 });
        });
        await using var host = builder.Build();
        await host.StartAsync();
        var preparation = ProtoTestAdapter.Prepare(TestMethods.Placeholder, host);
        var scope = await ProtoTestScope.StartAsync(preparation, host);
        scope.Result = ProtoTestResult.Passed;

        var failure = Assert.ThrowsAsync<ProtoCleanupException>(async () => await scope.DisposeAsync());

        // Teardown runs in reverse order, so beta fails first and alpha is the one left to count.
        Assert.Multiple(() =>
        {
            Assert.That(failure!.CleanupFailures, Has.Count.EqualTo(2));
            Assert.That(
                failure.Message,
                Is.EqualTo(
                    "The test body passed, but cleanup failed: InvalidOperationException: beta. 1 more cleanup failure."));
        });
    }

    [Test]
    public async Task StartAsync_WhenATokenIsSupplied_ShouldCarryItToTheContext()
    {
        await using var host = new ProtoHostBuilder().Build();
        await host.StartAsync();
        var preparation = ProtoTestAdapter.Prepare(TestMethods.Placeholder, host);
        using var cancellation = new CancellationTokenSource();

        var scope = await ProtoTestScope.StartAsync(preparation, host, null, cancellation.Token);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scope.Context.CancellationToken, Is.EqualTo(cancellation.Token),
                "the adapter's entry point carries the runner's token into the lifecycle");
            Assert.That(scope.Context.CancellationToken.CanBeCanceled, Is.True);
        }

        scope.Result = ProtoTestResult.Passed;
        await scope.DisposeAsync();
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
        // Completion reads the ambient context, so off-flow it would complete
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
    public async Task DisposeAsync_WhenDisposedOffFlow_ShouldReleaseTheOrphanedContext()
    {
        // The orphaned test never completes, but its clock entry must not leak with it: the scope
        // releases what it can before reporting the mismatch.
        await using var host = new ProtoHostBuilder().Build();
        await host.StartAsync();
        var preparation = ProtoTestAdapter.Prepare(TestMethods.Placeholder, host);
        var scope = await ProtoTestScope.StartAsync(preparation, host);
        scope.Result = ProtoTestResult.Passed;
        var testId = scope.Context.TestId;
        Assert.That(host.FindClock(testId), Is.Not.Null, "the starting test registers its clock");

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
            Assert.That(failure, Is.Not.Null, "the off-flow mismatch is still reported");
            Assert.That(host.FindClock(testId), Is.Null, "the orphaned clock entry is released");
        });
    }

    [Test]
    public async Task DisposeAsync_WhenAnotherHostsContextIsActive_ShouldRecordAFindingAndFail()
    {
        // A foreign active context would complete the wrong test; the scope
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

    private sealed class FailingTeardownHook(string message = "teardown failed") : IProtoTestHook
    {
        public int Order { get; init; }

        public Task BeforeTestAsync(ProtoExecutionContext context) => Task.CompletedTask;

        public Task AfterTestAsync(ProtoExecutionContext context)
            => throw new InvalidOperationException(message);
    }
}
