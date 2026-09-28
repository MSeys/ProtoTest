namespace ProtoTest.NUnit.Tests;

using ProtoTest.Core;

/// <summary>
/// Two cases of one <c>[ProtoTest]</c> method run in parallel: each must keep its own lifecycle even
/// though NUnit owns the attribute instance, and each must complete and record its own trace.
/// </summary>
[TestFixture]
public sealed class ParallelParameterizedTests
{
    [ProtoTest]
    [TestCase("alpha")]
    [TestCase("beta")]
    public async Task Case_ShouldKeepItsOwnLifecycle(string label)
    {
        await Task.Yield();
        Assert.That(Proto.Context.TestName, Does.Contain(nameof(Case_ShouldKeepItsOwnLifecycle)));
        Assert.That(label, Is.AnyOf("alpha", "beta"));
    }

    [OneTimeTearDown]
    public static void BothCases_ShouldHaveRecordedTheirOwnTrace()
    {
        // Each case traces under its NUnit full name, so the rows are distinguishable.
        var traces = ProtoTestAssembly.Host.Trace.Snapshot().Tests
            .Where(test => test.Name.Contains(nameof(Case_ShouldKeepItsOwnLifecycle), StringComparison.Ordinal))
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(traces, Has.Length.EqualTo(2), "both parameterized cases must complete their own lifecycle");
            Assert.That(traces.Select(test => test.Name).Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(2));
            Assert.That(traces.Select(test => test.Outcome), Is.All.EqualTo(ProtoTraceOutcome.Succeeded));
        });
    }
}
