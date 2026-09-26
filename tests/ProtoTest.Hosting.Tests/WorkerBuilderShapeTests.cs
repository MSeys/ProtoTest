namespace ProtoTest.Hosting.Tests;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProtoTest.Core;
using ProtoTest.Hosting.Internal;
using ProtoTest.Hosting.TestWorker;

/// <summary>
/// Pins the worker builder-shape boundary: the HostBuilding overlay is the
/// documented fallback when the entry point ignores its arguments, the application-builder shape is
/// matched through <see cref="IHostApplicationBuilder"/> (which <c>WebApplicationBuilder</c> also
/// implements), and an unrecognised builder fails loudly instead of silently losing the configuration
/// and the clock. A second worker program is disproportionate for the parameterless boundary, so it is
/// pinned at the same seam the resolver's HostBuilding event calls.
/// </summary>
[TestFixture]
public sealed class WorkerBuilderShapeTests
{
    [Test]
    public async Task ConfigureBuilder_WhenTheEntryPointIgnoresItsArguments_ShouldApplyTheOverlayAtBuild()
    {
        var seed = new DateTimeOffset(2026, 3, 3, 9, 0, 0, TimeSpan.Zero);
        var clock = new ProtoClock(seed);
        var worker = new ProtoWorkerHost<Program>(
            "Fallback",
            new ProtoWorkerOptions().Set("Worker:Value", "from-suite"),
            new ProtoWorkerRegistry());
        IHost? built = null;
        worker.UseFactory(_ =>
        {
            // What a parameterless Main does: build without args. The resolver's HostBuilding event
            // then invokes ConfigureBuilder before Build, exactly as this test does.
            var builder = Host.CreateDefaultBuilder();
            worker.ConfigureBuilder(builder);
            built = builder.Build();
            return built;
        });

        var context = new ProtoInfrastructureContext(
            new ProtoInfrastructureSettings(),
            new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Worker:Value"] = "from-config" }).Build(),
            clock);
        await worker.StartAsync(context);
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    built!.Services.GetRequiredService<IConfiguration>()["Worker:Value"],
                    Is.EqualTo("from-suite"),
                    "the in-memory overlay applies when the entry point ignored its arguments");
                Assert.That(
                    built.Services.GetRequiredService<TimeProvider>(),
                    Is.SameAs(clock),
                    "the worker's TimeProvider is replaced with the run's clock");
            });
        }
        finally
        {
            await built!.StopAsync();
            built.Dispose();
        }
    }

    [Test]
    public async Task ConfigureBuilder_ForAHostApplicationBuilder_ShouldApplyTheOverlay()
    {
        var seed = new DateTimeOffset(2026, 3, 3, 9, 0, 0, TimeSpan.Zero);
        var clock = new ProtoClock(seed);
        var worker = new ProtoWorkerHost<Program>(
            "ApplicationBuilder",
            new ProtoWorkerOptions().Set("Worker:Value", "from-suite"),
            new ProtoWorkerRegistry());
        string? seenBeforeBuild = null;
        IHost? built = null;
        worker.UseFactory(_ =>
        {
            var builder = Host.CreateApplicationBuilder();
            worker.ConfigureBuilder(builder);
            seenBeforeBuild = builder.Configuration["Worker:Value"];
            built = builder.Build();
            return built;
        });

        var context = new ProtoInfrastructureContext(
            new ProtoInfrastructureSettings(),
            new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Worker:Value"] = "from-config" }).Build(),
            clock);
        await worker.StartAsync(context);
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    seenBeforeBuild,
                    Is.EqualTo("from-suite"),
                    "the overlay lands on HostApplicationBuilder.Configuration before Build");
                Assert.That(
                    built!.Services.GetRequiredService<TimeProvider>(),
                    Is.SameAs(clock),
                    "the application builder's TimeProvider is replaced with the run's clock");
            });
        }
        finally
        {
            await built!.StopAsync();
            built.Dispose();
        }
    }

    [Test]
    public void ConfigureBuilder_ForAnUnrecognisedBuilder_ShouldExplain()
    {
        var worker = new ProtoWorkerHost<Program>("Unknown", new ProtoWorkerOptions(), new ProtoWorkerRegistry());

        var exception = Assert.Throws<InvalidOperationException>(() => worker.ConfigureBuilder(new object()));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain(typeof(object).FullName!), "the failure names the shape");
            Assert.That(exception.Message, Does.Contain("does not recognise"));
            Assert.That(exception.Message, Does.Contain("Host.CreateApplicationBuilder"));
        });
    }
}
