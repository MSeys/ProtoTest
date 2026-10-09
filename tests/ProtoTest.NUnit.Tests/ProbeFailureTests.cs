namespace ProtoTest.NUnit.Tests;

using global::NUnit.Framework.Interfaces;
using global::NUnit.Framework.Internal;
using global::NUnit.Framework.Internal.Commands;
using ProtoTest.AdapterContract;
using ProtoTest.Core;

/// <summary>
/// Characterization for the NUnit setup and teardown failure paths.
/// The command wrapper surfaces a setup failure with its original exception and records exactly one
/// failed trace; a teardown failure fails the result NUnit returns and lands as a failed trace with a
/// finding. Driven out of band so a deliberate failure cannot make the suite red.
/// </summary>
[TestFixture]
public sealed class ProbeFailureTests
{
    [Test]
    public void SetupFailure_ShouldSurfaceTheOriginalErrorAndRecordOneFailedTrace()
    {
        // Arrange
        var subject = SubjectTest(nameof(SetupFailure_ShouldSurfaceTheOriginalErrorAndRecordOneFailedTrace));
        var command = new ProtoTestAttribute().Wrap(new SuccessCommand(subject));

        // Act
        InvalidOperationException? exception = null;
        using (AdapterFailureProbe.BeginSetupFailure(subject.Method!.MethodInfo.Name))
        {
            try
            {
                command.Execute(TestExecutionContext.CurrentContext);
            }
            catch (InvalidOperationException caught)
            {
                exception = caught;
            }
        }

        // Assert
        var trace = TraceFor(subject);
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(AdapterFailureProbe.SetupMessage));
            Assert.That(trace.Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
            Assert.That(trace.Error!.Message, Does.Contain(AdapterFailureProbe.SetupMessage));
        });
        Assert.Throws<InvalidOperationException>(() => _ = Proto.Context);
    }

    [Test]
    public void TeardownFailure_ShouldFailTheResultAndNameTheCleanup()
    {
        // Arrange
        var subject = SubjectTest(nameof(TeardownFailure_ShouldFailTheResultAndNameTheCleanup));
        var command = new ProtoTestAttribute().Wrap(new SuccessCommand(subject));
        var context = TestExecutionContext.CurrentContext;
        var previousResult = context.CurrentResult;
        var subjectResult = subject.MakeTestResult();
        context.CurrentResult = subjectResult;

        // Act
        try
        {
            using (AdapterFailureProbe.BeginTeardownFailure(subject.Method!.MethodInfo.Name))
            {
                command.Execute(context);
            }
        }
        finally
        {
            context.CurrentResult = previousResult;
        }

        // Assert
        var trace = TraceFor(subject);
        Assert.Multiple(() =>
        {
            Assert.That(subjectResult.ResultState.Status, Is.EqualTo(TestStatus.Failed));
            Assert.That(subjectResult.Message, Does.Contain("The test body passed, but cleanup failed:"));
            Assert.That(subjectResult.Message, Does.Contain(AdapterFailureProbe.TeardownMessage));
            Assert.That(trace.Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
            Assert.That(trace.Error!.Message, Does.Contain("The test body passed, but cleanup failed:"));
            Assert.That(
                trace.Record!.Findings,
                Has.Some.Matches<ProtoTraceFindingRecord>(finding =>
                    finding.Message.Contains(AdapterFailureProbe.TeardownMessage)));
        });
    }

    private static TestMethod SubjectTest(string methodName)
        => new(new MethodWrapper(typeof(ProbeFailureTests), methodName));

    private static ProtoTestTrace TraceFor(Test subject)
        => ProtoTestAssembly.Host.Trace.Snapshot().Tests.Last(test => test.Name == subject.FullName);

    /// <summary>Stands in for the test body: a passing result, so the teardown path is reachable.</summary>
    private sealed class SuccessCommand(Test subject) : TestCommand(subject)
    {
        public override TestResult Execute(TestExecutionContext context)
        {
            context.CurrentResult.SetResult(ResultState.Success);
            return context.CurrentResult;
        }
    }
}
