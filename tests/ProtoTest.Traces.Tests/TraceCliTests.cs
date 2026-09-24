namespace ProtoTest.Traces.Tests;

using ProtoTest.Cli;

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
            var exit = CliHost.Run(["trace", "summary", path], output, error);

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
            Assert.That(error.ToString(), Does.Contain("usage: prototest trace summary"));
        });
    }

    [Test]
    public void MissingFile_ShouldReturnOneWithThePath()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = CliHost.Run(["trace", "summary", "does-not-exist.prototrace"], output, error);

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain("does-not-exist.prototrace"));
        });
    }
}
