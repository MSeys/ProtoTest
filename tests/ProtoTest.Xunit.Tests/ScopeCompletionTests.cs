namespace ProtoTest.Xunit.Tests;

using global::Xunit.Sdk;
using ProtoTest.AdapterContract;
using ProtoTest.Core;
using ProtoTest.Xunit.Sdk;

[Collection(ProtoTestCollection.Name)]
public sealed class ScopeCompletionTests
{
    [Fact]
    public async Task InnerRunnerThrow_ShouldCompleteTheScopeAsUnknown()
    {
        // xUnit v2 aggregates every throw a test body or lifecycle attribute produces, so nothing a
        // test does can make the inner runner throw. The completion boundary itself is what this
        // probe pins: InvokeAndCompleteAsync is the runner's real invocation-and-completion path, and
        // the throwing delegate stands in for xUnit's own pipeline failing after the scope started.
        var method = typeof(Subjects).GetMethod(nameof(Subjects.Probe))!;
        var preparation = ProtoTestAdapter.Prepare(method, ProtoTestAssembly.Host);
        AdapterLifecycle.ExpectUnknownTrace(preparation.TestName);

        var scope = await ProtoTestScope.StartAsync(
            preparation, ProtoTestAssembly.Host, Xunit2AttachmentPublisher.Instance);
        using var cancellation = new CancellationTokenSource();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ProtoXunitTestRunner.InvokeAndCompleteAsync(
                scope,
                new ExceptionAggregator(),
                cancellation,
                () => throw new InvalidOperationException("the inner runner threw")));

        Assert.Equal("the inner runner threw", exception.Message);
        Assert.Multiple(
            () =>
            {
                // The scope completed even though the invocation never produced a result: the trace
                // is Unknown, not missing, and nothing is left active on this flow.
                var trace = ProtoTestAssembly.Host.Trace.Snapshot().Tests.Last(test => test.Name == preparation.TestName);
                Assert.Equal(ProtoTraceOutcome.Unknown, trace.Outcome);
                Assert.Null(ProtoHost.CurrentContextOrNull);
            });
    }

    // Private, so xUnit's own discovery ignores it; the probe above drives it directly.
#pragma warning disable xUnit1000
    private sealed class Subjects
    {
        [ProtoTestFact]
        public void Probe()
        {
        }
    }
#pragma warning restore xUnit1000
}
