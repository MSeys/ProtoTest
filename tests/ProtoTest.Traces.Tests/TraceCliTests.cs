namespace ProtoTest.Traces.Tests;

using ProtoTest.Cli;
using ProtoTest.TestSupport;
using static ProtoTest.TestSupport.RecordedRuns;

/// <summary>The command surface: arguments, exit codes and the summary output.</summary>
[TestFixture]
public sealed class TraceCliTests
{
    [Test]
    public async Task TraceSummary_ShouldReturnZeroAndPrintTheDigest()
    {
        await TraceFixtures.WithTraceAsync(path =>
        {
            // Act
            using var output = new StringWriter();
            using var error = new StringWriter();
            var exit = CliHost.Run(["summary", path], output, error);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(exit, Is.EqualTo(0));
                Assert.That(output.ToString(), Does.Contain("FAILED reader fail"));
                Assert.That(output.ToString(), Does.Contain("The check failed."));
                Assert.That(error.ToString(), Is.Empty);
            }

            return Task.CompletedTask;
        });
    }

    [Test]
    public void UnknownCommand_ShouldReturnOneAndPrintUsage()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = CliHost.Run(["run"], output, error);

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain("usage: prototest summary"));
        });
    }

    [Test]
    public void MissingFile_ShouldReturnOneWithThePath()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = CliHost.Run(["summary", "does-not-exist.prototrace"], output, error);

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain("does-not-exist.prototrace"));
        });
    }

    [Test]
    public async Task Compare_ShouldReturnOneAndAnnotateABrokenTest()
    {
        using var baseline = new TemporaryTrace("cli-compare-baseline");
        using var current = new TemporaryTrace("cli-compare-current");
        await WriteAsync(baseline.Path, new RecordedTest("orders are listed", Call("List orders", "GET /orders")));
        await WriteAsync(
            current.Path,
            new RecordedTest("orders are listed", FailedCall("List orders", "GET /orders", new TimeoutException("No answer in 2 seconds."))));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = CliHost.Run(["compare", baseline.Path, current.Path], output, error);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(output.ToString(), Does.Contain("::error::broken: orders are listed"));
            Assert.That(output.ToString(), Does.Contain("BROKEN orders are listed (succeeded -> failed)"));
            Assert.That(error.ToString(), Is.Empty);
        }
    }

    [Test]
    public async Task Compare_ShouldReturnZeroWhenNothingBroke()
    {
        using var baseline = new TemporaryTrace("cli-compare-baseline");
        using var current = new TemporaryTrace("cli-compare-current");
        await WriteAsync(
            baseline.Path,
            new RecordedTest("orders are listed", FailedCall("List orders", "GET /orders", new TimeoutException("No answer."))));
        await WriteAsync(current.Path, new RecordedTest("orders are listed", Call("List orders", "GET /orders")));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = CliHost.Run(["compare", baseline.Path, current.Path], output, error);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exit, Is.EqualTo(0));
            Assert.That(output.ToString(), Does.Contain("FIXED orders are listed"));
        }
    }

    [Test]
    public void Compare_ShouldNameAMissingTrace()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = CliHost.Run(["compare", "baseline.prototrace", "current.prototrace"], output, error);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain("Trace file not found: baseline.prototrace"));
        }
    }
}
