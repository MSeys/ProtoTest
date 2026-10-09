namespace ProtoTest.Xunit3.Tests;

using ProtoTest.AdapterContract;
using ProtoTest.Core;
using Xunit;
using Xunit.v3;

/// <summary>
/// Characterization for the xUnit v3 setup and teardown failure paths.
/// The adapter surfaces a setup failure with its original exception and records one failed trace; a
/// teardown failure fails the test and lands as a failed trace with a finding. Driven through
/// the real lifecycle handler so a deliberate failure cannot make the suite red.
/// </summary>
public sealed class ProbeFailureTests
{
    [Fact]
    public void SetupFailure_ShouldSurfaceTheOriginalErrorAndRecordOneFailedTrace()
    {
        // Arrange
        var method = ProbeSubjects.SetupProbeMethod;

        // Act
        InvalidOperationException? exception;
        using (AdapterFailureProbe.BeginSetupFailure(nameof(ProbeSubjects.SetupProbe)))
        {
            exception = Assert.Throws<InvalidOperationException>(
                () => ProtoTestLifecycleHandler.Before(method, test: null));
        }

        // Assert
        var trace = AdapterLifecycle.FindTrace(ProtoTestAssembly.Host, method);
        Assert.Equal(AdapterFailureProbe.SetupMessage, exception!.Message);
        Assert.Equal(ProtoTraceOutcome.Failed, trace.Outcome);
        Assert.Contains(AdapterFailureProbe.SetupMessage, trace.Error?.Message);
        var contextFailure = Assert.Throws<InvalidOperationException>(() => _ = Proto.Context);
        Assert.Contains("FindTraceWriter", contextFailure.Message);
    }

    [Fact]
    public void TeardownFailure_ShouldFailTheTestAndNameTheCleanup()
    {
        // Arrange
        var method = ProbeSubjects.TeardownProbeMethod;
        var scope = ProtoTestLifecycleHandler.Before(method, test: null);

        // Act
        ProtoCleanupException? failure;
        using (AdapterFailureProbe.BeginTeardownFailure(nameof(ProbeSubjects.TeardownProbe)))
        {
            failure = Assert.Throws<ProtoCleanupException>(
                () => ProtoTestLifecycleHandler.Complete(scope, TestResultState.ForPassed(0m)));
        }

        // Assert: After throws, which is how xUnit v3 fails a test whose body already passed.
        var trace = AdapterLifecycle.FindTrace(ProtoTestAssembly.Host, method);
        Assert.Contains("The test body passed, but cleanup failed:", failure.Message);
        Assert.Contains(AdapterFailureProbe.TeardownMessage, failure.Message);
        Assert.Equal(ProtoTraceOutcome.Failed, trace.Outcome);
        Assert.Contains("The test body passed, but cleanup failed:", trace.Error?.Message);
        Assert.Contains(
            trace.Record!.Findings!,
            finding => finding.Message.Contains(AdapterFailureProbe.TeardownMessage));
    }

    private sealed class ProbeSubjects
    {
        public static readonly System.Reflection.MethodInfo SetupProbeMethod = typeof(ProbeSubjects).GetMethod(nameof(SetupProbe))!;
        public static readonly System.Reflection.MethodInfo TeardownProbeMethod = typeof(ProbeSubjects).GetMethod(nameof(TeardownProbe))!;

        public void SetupProbe()
        {
        }

        public void TeardownProbe()
        {
        }
    }
}
