namespace ProtoTest.Hosting.Tests;

using Microsoft.Extensions.Configuration;
using ProtoTest.Hosting.Internal;

/// <summary>
/// Pins the entry point's argument composition (audit CFG-1): the run's merged overlay travels as
/// command-line pairs, so a worker that builds its host from <c>args</c> reads final-precedence values
/// inside <c>Main</c>. A parameterless or argument-ignoring entry point is the documented limit: the
/// arguments are the only channel before <c>Build()</c>, where the <c>HostBuilding</c> overlay applies.
/// </summary>
[TestFixture]
public sealed class WorkerArgumentsTests
{
    [Test]
    public void Compose_ShouldCarryTheOverlayAsCommandLinePairsAndSkipNullValues()
    {
        var configuration = new Dictionary<string, string?>
        {
            ["Worker:Value"] = "from-suite",
            ["Worker:Missing"] = null,
            ["ConnectionStrings:WorkerProbe"] = "amqp://probe"
        };

        var arguments = ProtoWorkerArguments.Compose("C:\\worker", "Worker, Version=1.1.0.0", configuration);

        Assert.Multiple(() =>
        {
            Assert.That(arguments, Does.Contain("--contentRoot").And.Contain("C:\\worker"));
            Assert.That(arguments, Does.Contain("--applicationName").And.Contain("Worker, Version=1.1.0.0"));
            Assert.That(arguments, Does.Contain("--Worker:Value=from-suite"));
            Assert.That(arguments, Does.Contain("--ConnectionStrings:WorkerProbe=amqp://probe"));
            Assert.That(
                arguments.Any(argument => argument.StartsWith("--Worker:Missing", StringComparison.Ordinal)),
                Is.False,
                "a null value has no argument");
        });
    }

    [Test]
    public void Compose_ShouldBeWhatAnEntryPointReadingItsArgsSees()
    {
        var arguments = ProtoWorkerArguments.Compose("C:\\worker", "Worker", new Dictionary<string, string?>
        {
            ["Worker:Value"] = "from-suite",
            ["ConnectionStrings:WorkerProbe"] = "amqp://probe"
        });

        // What Host.CreateApplicationBuilder(args)/CreateDefaultBuilder(args) register: the command
        // line is the last source, so an entry point that passes its args to the builder sees the
        // run's final-precedence values.
        var configuration = new ConfigurationBuilder().AddCommandLine(arguments).Build();

        Assert.Multiple(() =>
        {
            Assert.That(configuration["Worker:Value"], Is.EqualTo("from-suite"));
            Assert.That(configuration.GetConnectionString("WorkerProbe"), Is.EqualTo("amqp://probe"));
            Assert.That(configuration["contentRoot"], Is.EqualTo("C:\\worker"));
            Assert.That(configuration["applicationName"], Is.EqualTo("Worker"));
        });
    }
}
