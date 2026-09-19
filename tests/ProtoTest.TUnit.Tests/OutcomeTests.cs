namespace ProtoTest.TUnit.Tests;

using System.Reflection;
using System.Runtime.CompilerServices;
using ProtoTest.Core;
using global::TUnit.Core.Executors;
using global::TUnit.Core.Interfaces;

public sealed class OutcomeTests
{
    [Test]
    [TestExecutor<PassthroughTestExecutor>]
    public async Task Executor_ShouldRecordSucceededForAPassingBody()
    {
        var trace = await RecordAsync(() => default);

        await Assert.That(trace.Outcome).IsEqualTo(ProtoTraceOutcome.Succeeded);
    }

    [Test]
    [TestExecutor<PassthroughTestExecutor>]
    public async Task Executor_ShouldRecordFailedForAFailingBody()
    {
        var trace = await RecordAsync(() => throw new InvalidOperationException("deliberate failure"));

        await Assert.That(trace.Outcome).IsEqualTo(ProtoTraceOutcome.Failed);
        await Assert.That(trace.Error!.Message).Contains("deliberate failure");
    }

    [Test]
    [TestExecutor<PassthroughTestExecutor>]
    public async Task Executor_ShouldRecordSkippedForABodySkip()
    {
        var trace = await RecordAsync(() =>
        {
            global::TUnit.Core.Skip.Test("deliberate body skip");
            return default;
        });

        await Assert.That(trace.Outcome).IsEqualTo(ProtoTraceOutcome.Skipped);
    }

    /// <summary>
    /// Runs the real <see cref="ProtoTestExecutor"/> around the given body while the passthrough executor
    /// keeps this driver itself outside a ProtoTest lifecycle, then returns the trace it recorded. The
    /// executor rethrows the body's failure after completing the trace, which the driver deliberately
    /// swallows so an intentionally failing body does not fail the suite.
    /// </summary>
    private static async Task<ProtoTestTrace> RecordAsync(Func<ValueTask> body, [CallerMemberName] string caller = "")
    {
        var name = ProtoTestName.FromMethod(
            typeof(OutcomeTests).GetMethod(caller, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!);
        var expectedCount = ProtoTestAssembly.Host.Trace.Snapshot().Tests.Count(test => test.Name == name) + 1;

        try
        {
            await new ProtoTestExecutor().ExecuteTest(global::TUnit.Core.TestContext.Current!, body);
        }
        catch
        {
            // Expected: the executor always rethrows what the body threw after it recorded the outcome.
        }

        var tests = ProtoTestAssembly.Host.Trace.Snapshot().Tests.Where(test => test.Name == name).ToList();
        await Assert.That(tests.Count).IsEqualTo(expectedCount);
        return tests[^1];
    }
}

/// <summary>Runs the driver test without the assembly-level <see cref="ProtoTestExecutor"/> wrapping it.</summary>
public sealed class PassthroughTestExecutor : ITestExecutor
{
    public ValueTask ExecuteTest(TestContext context, Func<ValueTask> action) => action();
}
