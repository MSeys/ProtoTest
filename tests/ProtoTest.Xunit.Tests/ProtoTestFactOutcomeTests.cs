namespace ProtoTest.Xunit.Tests;

using global::Xunit.Abstractions;
using global::Xunit.Sdk;
using ProtoTest.AdapterContract;
using ProtoTest.Core;
using ProtoTest.Xunit.Sdk;

[Collection(ProtoTestCollection.Name)]
public sealed class ProtoTestFactOutcomeTests
{
    [ProtoTestFact]
    public void ProtoTestFact_ShouldRunInsideAContextWithHooksApplied()
    {
        Assert.Equal(["Hook:Before"], Proto.Context.Resolve<ExecutionLogState>().Log);
        Assert.Equal("ProtoTest_Xunit_Success", Proto.Context.Service<ITestService>().GetMessage());
    }

    [ProtoTestTheory]
    [InlineData("first")]
    [InlineData("second")]
    public void ProtoTestTheory_ShouldRunEachRowInsideAContext(string row)
    {
        Assert.Contains(row, Proto.Context.TestName);
    }

    [Fact]
    public async Task PassingFact_ShouldRecordPassedOutcome()
    {
        var run = await RunFactAsync(nameof(Subjects.Passing));

        Assert.Equal(ProtoTraceOutcome.Succeeded, run.Trace.Outcome);
        Assert.Contains(run.Messages, message => message is ITestPassed);
    }

    [Fact]
    public async Task FailingFact_ShouldRecordFailedOutcomeWithTheAssertion()
    {
        var run = await RunFactAsync(nameof(Subjects.Failing));

        Assert.Equal(ProtoTraceOutcome.Failed, run.Trace.Outcome);
        Assert.Contains("deliberate failure", run.Trace.Error?.Message);
        Assert.Contains(run.Messages, message => message is ITestFailed);
    }

    [Fact]
    public async Task FailingAsyncFact_ShouldRecordFailedOutcome()
    {
        var run = await RunFactAsync(nameof(Subjects.FailingAsync));

        Assert.Equal(ProtoTraceOutcome.Failed, run.Trace.Outcome);
        Assert.Equal(typeof(InvalidOperationException).FullName, run.Trace.Error?.Type);
    }

    [Fact]
    public async Task SkippedFact_ShouldReportSkippedToTheRunnerInsteadOfPassed()
    {
        var testCase = new ProtoXunitTestCase(
            new NullMessageSink(),
            TestMethodDisplay.ClassAndMethod,
            TestMethodDisplayOptions.None,
            TestMethod(nameof(Subjects.RequiresCapability)));
        var bus = new RecordingMessageBus();

        var summary = await testCase.RunAsync(
            new NullMessageSink(), bus, [], new ExceptionAggregator(), new CancellationTokenSource());

        Assert.Equal(1, summary.Total);
        Assert.Equal(1, summary.Skipped);
        Assert.Equal(0, summary.Failed);
        Assert.Contains(bus.Messages, message => message is ITestSkipped);
        Assert.DoesNotContain(bus.Messages, message => message is ITestPassed);
        // The skip happens before the lifecycle starts, so no trace was opened for it.
        Assert.DoesNotContain(
            ProtoTestAssembly.Host.Trace.Snapshot().Tests,
            test => test.Name.Contains(nameof(Subjects.RequiresCapability), StringComparison.Ordinal));
    }

    [Fact]
    public async Task SetupFailure_ShouldReportFailedToTheRunnerAndRecordOneFailedTrace()
    {
        var testCase = new ProtoXunitTestCase(
            new NullMessageSink(),
            TestMethodDisplay.ClassAndMethod,
            TestMethodDisplayOptions.None,
            TestMethod(nameof(Subjects.SetupProbe)));
        var bus = new RecordingMessageBus();

        using (AdapterFailureProbe.BeginSetupFailure(nameof(Subjects.SetupProbe)))
        {
            await testCase.RunAsync(new NullMessageSink(), bus, [], new ExceptionAggregator(), new CancellationTokenSource());
        }

        var trace = TraceFor(testCase.DisplayName);
        Assert.Equal(ProtoTraceOutcome.Failed, trace.Outcome);
        Assert.Contains(AdapterFailureProbe.SetupMessage, trace.Error?.Message);
        Assert.Contains(bus.Messages, message => message is ITestFailed);
    }

    [Fact]
    public async Task TeardownFailure_ShouldReportPassedToTheRunnerAndRecordPartial()
    {
        var testCase = new ProtoXunitTestCase(
            new NullMessageSink(),
            TestMethodDisplay.ClassAndMethod,
            TestMethodDisplayOptions.None,
            TestMethod(nameof(Subjects.TeardownProbe)));
        var bus = new RecordingMessageBus();

        using (AdapterFailureProbe.BeginTeardownFailure(nameof(Subjects.TeardownProbe)))
        {
            await testCase.RunAsync(new NullMessageSink(), bus, [], new ExceptionAggregator(), new CancellationTokenSource());
        }

        var trace = TraceFor(testCase.DisplayName);
        Assert.Equal(ProtoTraceOutcome.Partial, trace.Outcome);
        Assert.Null(trace.Error);
        Assert.Contains(
            trace.Record!.Findings!,
            finding => finding.Message.Contains(AdapterFailureProbe.TeardownMessage));
        Assert.Contains(bus.Messages, message => message is ITestPassed);
    }

    [Fact]
    public async Task CancelledFact_ShouldRecordCancelledOutcome()
    {
        var testCase = new ProtoXunitTestCase(
            new NullMessageSink(),
            TestMethodDisplay.ClassAndMethod,
            TestMethodDisplayOptions.None,
            TestMethod(nameof(Subjects.Cancelled)));
        var bus = new RecordingMessageBus();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await testCase.RunAsync(new NullMessageSink(), bus, [], new ExceptionAggregator(), cancellation);

        var trace = TraceFor(testCase.DisplayName);
        Assert.Equal(ProtoTraceOutcome.Cancelled, trace.Outcome);
    }

    [Fact]
    public async Task CancelledBody_ShouldRecordCancelledOutcome()
    {
        // A body OperationCanceledException maps to Cancelled in xUnit v2, matching MSTest,
        // TUnit and xUnit v3.
        var run = await RunFactAsync(nameof(Subjects.Cancelled));

        Assert.Equal(ProtoTraceOutcome.Cancelled, run.Trace.Outcome);
    }

    [Fact]
    public async Task TheoryRows_ShouldEachRecordTheirOwnOutcome()
    {
        var method = TestMethod(nameof(Subjects.EvenOnly));
        var theory = method.Method.GetCustomAttributes(typeof(TheoryAttribute)).Single();
        var testCases = new ProtoTestTheoryDiscoverer(new NullMessageSink())
            .Discover(new DiscoveryOptions(), method, theory)
            .ToArray();

        var bus = new RecordingMessageBus();
        foreach (var testCase in testCases)
        {
            await testCase.RunAsync(new NullMessageSink(), bus, [], new ExceptionAggregator(), new CancellationTokenSource());
        }

        var outcomes = testCases.ToDictionary(
            testCase => testCase.DisplayName,
            testCase => TraceFor(testCase.DisplayName).Outcome);
        Assert.Equal(2, testCases.Length);
        Assert.All(testCases, testCase => Assert.IsType<ProtoXunitTestCase>(testCase));
        Assert.Equal(ProtoTraceOutcome.Succeeded, outcomes.Single(pair => pair.Key.Contains("value: 2")).Value);
        Assert.Equal(ProtoTraceOutcome.Failed, outcomes.Single(pair => pair.Key.Contains("value: 3")).Value);
    }

    private static async Task<(ProtoTestTrace Trace, IReadOnlyList<IMessageSinkMessage> Messages)> RunFactAsync(string methodName)
    {
        var testCase = new ProtoXunitTestCase(
            new NullMessageSink(),
            TestMethodDisplay.ClassAndMethod,
            TestMethodDisplayOptions.None,
            TestMethod(methodName));
        var bus = new RecordingMessageBus();

        await testCase.RunAsync(new NullMessageSink(), bus, [], new ExceptionAggregator(), new CancellationTokenSource());

        return (TraceFor(testCase.DisplayName), bus.Messages);
    }

    private static ProtoTestTrace TraceFor(string displayName)
        => ProtoTestAssembly.Host.Trace.Snapshot().Tests.Last(test => test.Name == displayName);

    private static ITestMethod TestMethod(string methodName)
    {
        var assembly = new TestAssembly(Reflector.Wrap(typeof(Subjects).Assembly));
        var collection = new TestCollection(assembly, null, "ProtoTest outcome subjects");
        var testClass = new TestClass(collection, Reflector.Wrap(typeof(Subjects)));
        return new global::Xunit.Sdk.TestMethod(testClass, Reflector.Wrap(typeof(Subjects).GetMethod(methodName)!));
    }

    // Private, so xUnit's own discovery ignores these; they're only run explicitly above.
#pragma warning disable xUnit1000
    private sealed class Subjects
    {
        [ProtoTestFact]
        public void Passing() => Assert.NotNull(Proto.Context);

        [ProtoTestFact]
        public void Failing() => Assert.Fail("deliberate failure");

        [ProtoTestFact]
        public async Task FailingAsync()
        {
            await Task.Yield();
            throw new InvalidOperationException("async failure");
        }

        [ProtoTestTheory]
        [InlineData(2)]
        [InlineData(3)]
        public void EvenOnly(int value) => Assert.Equal(0, value % 2);

        // Only the probe drivers above run these; the failure hook must not fire on them.
        [ProtoTestFact]
        public void SetupProbe()
        {
        }

        [ProtoTestFact]
        public void TeardownProbe()
        {
        }

        [ProtoTestFact]
        [RequiresCapability(AdapterProbes.SkipCapability, Reason = AdapterProbes.SkipReason)]
        public void RequiresCapability()
            => throw new InvalidOperationException("A skipped test must not run its body.");

        [ProtoTestFact]
        public void Cancelled() => throw new OperationCanceledException("deliberate cancellation");
    }

#pragma warning restore xUnit1000

    private sealed class DiscoveryOptions : ITestFrameworkDiscoveryOptions
    {
        public TValue GetValue<TValue>(string name) => default!;

        public void SetValue<TValue>(string name, TValue value)
        {
        }
    }

    private sealed class RecordingMessageBus : IMessageBus
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<IMessageSinkMessage> _messages = new();

        public IReadOnlyList<IMessageSinkMessage> Messages => [.. _messages];

        public bool QueueMessage(IMessageSinkMessage message)
        {
            _messages.Enqueue(message);
            return true;
        }

        public void Dispose()
        {
        }
    }
}
