namespace ProtoTest.NUnit.Tests;

using global::NUnit.Framework.Internal;
using global::NUnit.Framework.Internal.Commands;
using ProtoTest.Core;

/// <summary>
/// Pins the outcome mapping through NUnit's real test command: the subject body actually runs, so the
/// exception the runner would record is the one the adapter sees. A stub command that only sets a
/// result state cannot catch a body that throws before NUnit recorded anything.
/// </summary>
[TestFixture]
public sealed class RealRunnerOutcomeTests
{
    [Test]
    public void PassingBody_ShouldRecordSucceededOutcome()
    {
        var (trace, escaped) = RunSubject(nameof(Subjects.Passing));

        Assert.Multiple(() =>
        {
            Assert.That(escaped, Is.Null);
            Assert.That(trace.Outcome, Is.EqualTo(ProtoTraceOutcome.Succeeded));
            Assert.That(trace.Error, Is.Null);
        });
    }

    [Test]
    public void FailingBody_ShouldRecordFailedOutcomeWithTheException()
    {
        var (trace, escaped) = RunSubject(nameof(Subjects.Failing));

        Assert.Multiple(() =>
        {
            Assert.That(escaped, Is.TypeOf<InvalidOperationException>(), "the runner still sees the failure");
            Assert.That(trace.Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
            Assert.That(trace.Error!.Type, Is.EqualTo(typeof(InvalidOperationException).FullName));
            Assert.That(trace.Error.Message, Does.Contain("deliberate failure"));
            Assert.That(trace.Error.StackTrace, Does.Contain(nameof(Subjects.Failing)));
        });
    }

    [Test]
    public void IgnoringBody_ShouldRecordSkippedOutcome()
    {
        var (trace, escaped) = RunSubject(nameof(Subjects.Ignoring));

        Assert.Multiple(() =>
        {
            Assert.That(escaped, Is.TypeOf<IgnoreException>());
            Assert.That(trace.Outcome, Is.EqualTo(ProtoTraceOutcome.Skipped));
        });
    }

    /// <summary>
    /// Runs a subject through NUnit's real <see cref="TestMethodCommand"/>, the command the runner
    /// executes, and finishes the way the work item does: an exception that escapes the command chain
    /// is recorded on the runner's result. The trace is what the adapter completed before it left.
    /// </summary>
    private static (ProtoTestTrace Trace, Exception? Escaped) RunSubject(string subjectName)
    {
        var subject = new TestMethod(new MethodWrapper(typeof(Subjects), subjectName));
        var command = new ProtoTestAttribute().Wrap(new TestMethodCommand(subject));

        var context = TestExecutionContext.CurrentContext;
        var previousResult = context.CurrentResult;
        var previousObject = context.TestObject;
        context.CurrentResult = subject.MakeTestResult();
        context.TestObject = new Subjects();
        Exception? escaped = null;
        try
        {
            try
            {
                command.Execute(context);
            }
            catch (Exception exception)
            {
                // RecordException unwraps the reflection wrapper, so the runner reports the original.
                context.CurrentResult.RecordException(exception);
                escaped = exception.Unwrap();
            }
        }
        finally
        {
            context.CurrentResult = previousResult;
            context.TestObject = previousObject;
        }

        return (ProtoTestAssembly.Host.Trace.Snapshot().Tests.Last(test => test.Name == subject.FullName), escaped);
    }

    // Private, so NUnit's own discovery ignores these; the driver runs them through the real command.
    private sealed class Subjects
    {
        public void Passing()
        {
        }

        public void Failing() => throw new InvalidOperationException("deliberate failure");

        public void Ignoring() => Assert.Ignore("deliberate skip");
    }
}
