namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The run setup seam (Audit 5 A5.7b): a step is infrastructure that starts at its registration
/// position, reads the settings the pieces before it published, and fails the run's start loudly
/// without owning anything stop or dispose must release.
/// </summary>
[TestFixture]
public sealed class RunSetupTests
{
    private const string AddressKey = "ConnectionStrings:Orders";

    [Test]
    public async Task AddRunSetup_ShouldRunAtItsRegistrationPositionAndSeeEarlierSettings()
    {
        var events = new List<string>();
        bool? seenBefore = null;
        string? seenAfter = null;
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddRunSetup("before", setup =>
        {
            events.Add("before");
            seenBefore = setup.Settings.Values.ContainsKey(AddressKey);
            return ValueTask.CompletedTask;
        });
        builder.AddInfrastructure(
            new DeclaredSettingsInfrastructure("database:orders", AddressKey, "Host=container"),
            AddressKey);
        builder.AddRunSetup("after", setup =>
        {
            events.Add("after");
            seenAfter = setup.Settings.Values[AddressKey];
            return ValueTask.CompletedTask;
        });
        await using var host = builder.Build();

        await host.StartAsync();

        var entity = host.Trace.Snapshot().Entities!.Single(candidate => candidate.Id == "setup:after");
        Assert.Multiple(() =>
        {
            Assert.That(
                events,
                Is.EqualTo(new[] { "before", "after" }),
                "steps start at their registration positions in the infrastructure order");
            Assert.That(
                seenBefore,
                Is.False,
                "a step registered before the publisher cannot see the key it fills");
            Assert.That(
                seenAfter,
                Is.EqualTo("Host=container"),
                "a step registered after the publisher reads the started value");
            Assert.That(
                entity.State["infrastructure.kind"],
                Is.EqualTo("setup"),
                "the step is recorded as a run entity like any other piece");
            Assert.That(
                entity.Versions!.Last().Change,
                Is.EqualTo("started"),
                "the run entity records that the step started");
        });

        await host.StopAsync();
    }

    [Test]
    public async Task AddRunSetup_WhenTheStepThrows_ShouldFailTheRunStartAndLeaveTheHostRetryable()
    {
        var attempts = 0;
        var hook = new TrackingRunHook();
        ProtoInfrastructureSettings? settings = null;
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureServices(services => services.AddSingleton<IProtoRunHook>(hook));
        builder.AddInfrastructure(
            new DeclaredSettingsInfrastructure("database:orders", AddressKey, "Host=container"),
            AddressKey);
        builder.AddRunSetup("schema", setup =>
        {
            attempts++;
            settings = setup.Settings;
            return attempts == 1
                ? ValueTask.FromException(new InvalidOperationException("The schema step failed."))
                : ValueTask.CompletedTask;
        });
        await using var host = builder.Build();

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await host.StartAsync());

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Message,
                Is.EqualTo("The schema step failed."),
                "the step's own exception is the run's start failure");
            Assert.That(
                hook.AfterRunCount,
                Is.EqualTo(1),
                "a completed user hook unwinds on the failed start, as for failing infrastructure");
            Assert.That(
                settings!.Values,
                Is.Empty,
                "settings a released piece filled do not survive the rollback");
        });

        // Created again: the retry starts the pieces, runs the step again and unwinds at the real end.
        await host.StartAsync();
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(attempts, Is.EqualTo(2), "the retry runs the step again");
            Assert.That(hook.AfterRunCount, Is.EqualTo(2), "the retried run unwinds at its real end");
        });
    }

    [Test]
    public async Task AddRunSetup_AfterTheRunStopped_ShouldNotRunAgainOnStopOrDispose()
    {
        var runs = 0;
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddRunSetup("schema", _ =>
        {
            runs++;
            return ValueTask.CompletedTask;
        });
        var host = builder.Build();

        await host.StartAsync();
        await host.StopAsync();
        await host.DisposeAsync();

        Assert.That(
            runs,
            Is.EqualTo(1),
            "the step owns nothing to release; stop and dispose do not call it again");
    }

    [Test]
    public void AddRunSetup_ShouldRejectAnInvalidRegistration()
    {
        var builder = new ProtoHostBuilder();

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => builder.AddRunSetup(" ", _ => ValueTask.CompletedTask));
            Assert.Throws<ArgumentNullException>(() => builder.AddRunSetup("schema", null!));
        });
    }

    [Test]
    public async Task AddRunSetup_WithADuplicateName_ShouldThrow()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddRunSetup("schema", _ => ValueTask.CompletedTask);

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.AddRunSetup("schema", _ => ValueTask.CompletedTask));

        Assert.That(exception!.Message, Does.Contain("setup:schema"));
        await using var host = builder.Build();
    }

    [Test]
    public async Task AddRunSetup_AfterBuild_ShouldThrow()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        await using var host = builder.Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.AddRunSetup("schema", _ => ValueTask.CompletedTask));

        Assert.That(exception!.Message, Does.Contain("already built"));
    }

    private sealed class TrackingRunHook : IProtoRunHook
    {
        public int AfterRunCount { get; private set; }

        public Task AfterRunAsync(CancellationToken cancellationToken = default)
        {
            AfterRunCount++;
            return Task.CompletedTask;
        }
    }
}
