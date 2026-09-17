namespace ProtoTest.Xunit.Tests;

using global::Xunit.Abstractions;
using global::Xunit.Sdk;
using ProtoTest.Core;
using ProtoTest.Xunit.Sdk;

[Collection(ProtoTestCollection.Name)]
public sealed class ProtoTestFactOutcomeTests
{
    [ProtoTestFact]
    public void ProtoTestFact_ShouldRunInsideAContextWithHooksApplied()
    {
        Assert.Equal(["Hook:Before"], Proto.Context.Context<ExecutionLogState>().Log);
        Assert.Equal("ProtoTest_Xunit_Success", Proto.Context.Service<ITestService>().GetValue());
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
