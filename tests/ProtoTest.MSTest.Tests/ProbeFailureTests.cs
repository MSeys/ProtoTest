namespace ProtoTest.MSTest.Tests;

using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProtoTest.AdapterContract;
using ProtoTest.Core;

/// <summary>
/// Characterization for the MSTest setup and teardown failure paths.
/// The adapter surfaces a setup failure with its original exception and records exactly one failed
/// trace; a teardown failure keeps the reported result and lands as a Partial trace with a finding.
/// </summary>
[TestClass]
public sealed class ProbeFailureTests
{
    [TestMethod]
    public async Task SetupFailure_ShouldSurfaceTheOriginalErrorAndRecordOneFailedTrace()
    {
        // Arrange
        var method = ProbeSubjects.SetupProbeMethod;

        // Act
        InvalidOperationException? exception = null;
        using (AdapterFailureProbe.BeginSetupFailure(nameof(ProbeSubjects.SetupProbe)))
        {
            try
            {
                await new ProtoTestAttribute().ExecuteAsync(new FakeTestMethod(method));
            }
            catch (InvalidOperationException caught)
            {
                exception = caught;
            }
        }

        // Assert
        var trace = AdapterLifecycle.FindTrace(ProtoTestAssembly.Host, method);
        Assert.IsNotNull(exception);
        Assert.AreEqual(AdapterFailureProbe.SetupMessage, exception.Message);
        Assert.AreEqual(ProtoTraceOutcome.Failed, trace.Outcome);
        StringAssert.Contains(trace.Error?.Message, AdapterFailureProbe.SetupMessage);
        Assert.IsFalse(TryGetContext());
    }

    [TestMethod]
    public async Task TeardownFailure_ShouldKeepTheResultAndRecordAPartialTrace()
    {
        // Arrange
        var method = ProbeSubjects.TeardownProbeMethod;

        // Act: the teardown failure is swallowed by the scope, exactly as a runner's per-test cleanup is.
        using (AdapterFailureProbe.BeginTeardownFailure(nameof(ProbeSubjects.TeardownProbe)))
        {
            await new ProtoTestAttribute().ExecuteAsync(new FakeTestMethod(method));
        }

        // Assert
        var trace = AdapterLifecycle.FindTrace(ProtoTestAssembly.Host, method);
        Assert.AreEqual(ProtoTraceOutcome.Partial, trace.Outcome);
        Assert.IsNull(trace.Error, "a teardown failure is not the test's own error");
        Assert.IsTrue(
            trace.Record!.Findings!.Any(finding => finding.Message.Contains(AdapterFailureProbe.TeardownMessage)));
    }

    private static bool TryGetContext()
    {
        try
        {
            _ = Proto.Context;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    // Private, so MSTest's own discovery ignores these; only the fake ITestMethod above runs them.
#pragma warning disable MSTEST0030
    private sealed class ProbeSubjects
    {
        public static readonly MethodInfo SetupProbeMethod = typeof(ProbeSubjects).GetMethod(nameof(SetupProbe))!;
        public static readonly MethodInfo TeardownProbeMethod = typeof(ProbeSubjects).GetMethod(nameof(TeardownProbe))!;

        [ProtoTest]
        public void SetupProbe()
        {
        }

        [ProtoTest]
        public void TeardownProbe()
        {
        }
    }
#pragma warning restore MSTEST0030
}
