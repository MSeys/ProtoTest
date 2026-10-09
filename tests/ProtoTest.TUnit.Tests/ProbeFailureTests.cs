namespace ProtoTest.TUnit.Tests;

using System.Reflection;
using System.Runtime.CompilerServices;
using global::TUnit.Core.Executors;
using ProtoTest.AdapterContract;
using ProtoTest.Core;

/// <summary>
/// Characterization for the TUnit setup and teardown failure paths.
/// The executor surfaces a setup failure with its original exception and records one failed trace; a
/// teardown failure is rethrown so TUnit fails the test, and the trace records that failure.
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
    public async Task TeardownFailure_ShouldFailTheTestAndNameTheCleanup()
    {
        // Act
        var (trace, failure) = await RunTeardownProbeAsync();

        // Assert
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.Message).Contains("The test body passed, but cleanup failed:");
        await Assert.That(failure.Message).Contains(AdapterFailureProbe.TeardownMessage);
        await Assert.That(trace.Outcome).IsEqualTo(ProtoTraceOutcome.Failed);
        await Assert.That(trace.Error!.Message).Contains("The test body passed, but cleanup failed:");
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

    private static async Task<(ProtoTestTrace Trace, ProtoCleanupException? Failure)> RunTeardownProbeAsync(
        [CallerMemberName] string caller = "")
    {
        var method = typeof(ProbeFailureTests).GetMethod(caller, BindingFlags.Instance | BindingFlags.Public)!;
        ProtoCleanupException? failure = null;
        using (AdapterFailureProbe.BeginTeardownFailure(caller))
        {
            try
            {
                await new ProtoTestExecutor().ExecuteTest(global::TUnit.Core.TestContext.Current!, () => default);
            }
            catch (ProtoCleanupException caught)
            {
                failure = caught;
            }
        }

        return (AdapterLifecycle.FindTrace(ProtoTestAssembly.Host, method), failure);
    }
}
