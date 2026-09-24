namespace ProtoTest.TUnit.Tests;

using System.Reflection;
using System.Runtime.CompilerServices;
using global::TUnit.Core.Executors;
using ProtoTest.AdapterContract;
using ProtoTest.Core;

/// <summary>
/// Stage 0 characterization for the TUnit setup and teardown failure paths (Audit 3, findings E5/F1).
/// The executor surfaces a setup failure with its original exception and records one failed trace; a
/// teardown failure keeps the reported result and lands as a Partial trace with a finding.
/// </summary>
public sealed class ProbeFailureTests
{
    [Test]
    [TestExecutor<PassthroughTestExecutor>]
    public async Task SetupFailure_ShouldSurfaceTheOriginalErrorAndRecordOneFailedTrace()
    {
        // Act
        var (trace, failure) = await RunSetupProbeAsync();

        // Assert
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.Message).IsEqualTo(AdapterFailureProbe.SetupMessage);
        await Assert.That(trace.Outcome).IsEqualTo(ProtoTraceOutcome.Failed);
        await Assert.That(trace.Error!.Message).Contains(AdapterFailureProbe.SetupMessage);
    }

    [Test]
    [TestExecutor<PassthroughTestExecutor>]
    public async Task TeardownFailure_ShouldKeepTheResultAndRecordAPartialTrace()
    {
        // Act: the teardown failure is swallowed by the scope, so the executor completes normally.
        var trace = await RunTeardownProbeAsync();

        // Assert
        await Assert.That(trace.Outcome).IsEqualTo(ProtoTraceOutcome.Partial);
        await Assert.That(trace.Error).IsNull();
        await Assert.That(
            trace.Record!.Findings!.Any(finding => finding.Message.Contains(AdapterFailureProbe.TeardownMessage)))
            .IsTrue();
    }

    private static async Task<(ProtoTestTrace Trace, InvalidOperationException? Failure)> RunSetupProbeAsync(
        [CallerMemberName] string caller = "")
    {
        var method = typeof(ProbeFailureTests).GetMethod(caller, BindingFlags.Instance | BindingFlags.Public)!;
        InvalidOperationException? failure = null;
        using (AdapterFailureProbe.BeginSetupFailure(caller))
        {
            try
            {
                await new ProtoTestExecutor().ExecuteTest(global::TUnit.Core.TestContext.Current!, () => default);
            }
            catch (InvalidOperationException caught)
            {
                failure = caught;
            }
        }

        return (AdapterLifecycle.FindTrace(ProtoTestAssembly.Host, method), failure);
    }

    private static async Task<ProtoTestTrace> RunTeardownProbeAsync([CallerMemberName] string caller = "")
    {
        var method = typeof(ProbeFailureTests).GetMethod(caller, BindingFlags.Instance | BindingFlags.Public)!;
        using (AdapterFailureProbe.BeginTeardownFailure(caller))
        {
            await new ProtoTestExecutor().ExecuteTest(global::TUnit.Core.TestContext.Current!, () => default);
        }

        return AdapterLifecycle.FindTrace(ProtoTestAssembly.Host, method);
    }
}
