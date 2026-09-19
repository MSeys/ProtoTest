namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.DependencyInjection;

[TestFixture]
public sealed class RegistrationIdempotencyTests
{
    [Test]
    public async Task AddResource_ShouldKeepTheSameInstanceRegisteredTwiceOnce()
    {
        var released = new List<string>();
        var resource = new ProtoResource(
            "database:container",
            "database",
            "Postgres container",
            _ =>
            {
                released.Add("shared");
                return ValueTask.CompletedTask;
            },
            ProtoResourceScope.Run);
        var builder = new ProtoHostBuilder();
        builder.AddResource(resource);
        builder.AddResource(resource);

        await using var host = builder.Build();
        await host.StartAsync();
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(released, Is.EqualTo(new[] { "shared" }));
            Assert.That(
                host.Trace.Snapshot().Entries!.Count(entry => entry.Kind == "resource.owned"),
                Is.EqualTo(1));
        });
    }

    [Test]
    public void AddResource_ShouldRejectADifferentInstanceWithTheSameId()
    {
        var builder = new ProtoHostBuilder();
        builder.AddResource(new ProtoResource(
            "database:container", "database", "Postgres container", _ => default, ProtoResourceScope.Run));

        var exception = Assert.Throws<InvalidOperationException>(() => builder.AddResource(new ProtoResource(
            "database:container", "database", "Another Postgres container", _ => default, ProtoResourceScope.Run)));

        Assert.That(exception!.Message, Does.Contain("already owned by the run"));
        Assert.That(exception.Message, Does.Not.Contain("ProtoExecutionContext"));
    }

    [Test]
    public async Task AddCapability_ShouldRegisterAnEqualDescriptorOnce()
    {
        var builder = new ProtoHostBuilder();
        IServiceCollection? services = null;
        builder.ConfigureServices(collection => services = collection);
        var capability = new ProtoCapabilityDescriptor("Stub", "protocol", "Tests");
        builder.AddCapability(capability);
        builder.AddCapability(capability);

        await using var host = builder.Build();

        Assert.Multiple(() =>
        {
            Assert.That(
                services!.Count(descriptor => descriptor.ServiceType == typeof(ProtoCapabilityDescriptor)),
                Is.EqualTo(1));
            Assert.That(host.HasCapability("protocol", "Stub"), Is.True);
        });
    }

    [Test]
    public async Task AddInfrastructure_ShouldStartAndReleaseTheSameResourceOnce()
    {
        var infrastructure = new StubInfrastructure("database:stub");
        var builder = new ProtoHostBuilder();
        builder.AddInfrastructure(infrastructure);
        builder.AddInfrastructure(infrastructure);

        await using var host = builder.Build();
        await host.StartAsync();
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(infrastructure.StartCount, Is.EqualTo(1));
            Assert.That(infrastructure.ReleaseCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task AddInfrastructure_ShouldMergeSettingsKeysFromRepeatedRegistration()
    {
        var infrastructure = new StubConnectionInfrastructure("database:stub");
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddInfrastructure(infrastructure, "ConnectionStrings:Thing");
        builder.AddInfrastructure(infrastructure, "ProtoTest:Thing:Connection");

        await using var host = builder.Build();
        await host.StartAsync();

        var entity = host.Trace.Snapshot().Entities!.Single(candidate => candidate.Id == "database:stub");
        Assert.Multiple(() =>
        {
            Assert.That(entity.State["infrastructure.settings"], Does.Contain("ConnectionStrings:Thing"));
            Assert.That(entity.State["infrastructure.settings"], Does.Contain("ProtoTest:Thing:Connection"));
        });
    }

    [Test]
    public async Task AddSink_ShouldRegisterTheSameInstanceOnce()
    {
        var sink = new CountingSink();
        var builder = new ProtoHostBuilder();
        builder.AddSink(sink);
        builder.AddSink(sink);

        await using var host = builder.Build();
        await host.StartAsync();
        await host.StopAsync();

        Assert.That(sink.ExportCount, Is.EqualTo(1));
    }

    [Test]
    public async Task AddInfrastructure_ShouldLeaveNoSettingsWhenAConflictingInstanceIsRejected()
    {
        var first = new StubConnectionInfrastructure("database:stub");
        var conflicting = new StubConnectionInfrastructure("database:stub");
        SettingsProbe? probe = null;
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddInfrastructure(first, "ConnectionStrings:First");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            builder.AddInfrastructure(conflicting, "ConnectionStrings:Second"));
        Assert.That(exception!.Message, Does.Contain("already owned by the run"));

        builder.ConfigureServices(services => services.AddSingleton<IProtoRunHook>(provider =>
        {
            probe = new SettingsProbe(provider.GetRequiredService<ProtoInfrastructureSettings>());
            return probe;
        }));
        await using var host = builder.Build();
        await host.StartAsync();

        // The rejected registration's key must not have been merged into the accepted one.
        Assert.Multiple(() =>
        {
            Assert.That(probe!.Values.Keys, Does.Contain("ConnectionStrings:First"));
            Assert.That(probe.Values.Keys, Does.Not.Contain("ConnectionStrings:Second"));
        });
    }

    [Test]
    public async Task AddSink_ShouldApplyEveryConfigureCallbackToTheSingleRegistration()
    {
        SinkProbe? probe = null;
        var builder = new ProtoHostBuilder();
        builder.AddSink<ConfigurableSink>(sink => sink.Configured.Add("first"));
        builder.AddSink<ConfigurableSink>(sink => sink.Configured.Add("second"));
        builder.ConfigureServices(services => services.AddSingleton<IProtoRunHook>(provider =>
        {
            probe = new SinkProbe(provider.GetServices<IProtoSink>());
            return probe;
        }));

        await using var host = builder.Build();
        await host.StartAsync();
        await host.StopAsync();

        var sink = probe!.Sinks.OfType<ConfigurableSink>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(sink.Configured, Is.EqualTo(new[] { "first", "second" }), "the second configure is not dropped");
            Assert.That(sink.ExportCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task AddSink_ShouldNotDuplicateASinkRegisteredDirectly()
    {
        SinkProbe? probe = null;
        var builder = new ProtoHostBuilder();
        builder.ConfigureServices(services => services.AddSingleton<IProtoSink, ConfigurableSink>());
        builder.AddSink<ConfigurableSink>(sink => sink.Configured.Add("configured"));
        builder.ConfigureServices(services => services.AddSingleton<IProtoRunHook>(provider =>
        {
            probe = new SinkProbe(provider.GetServices<IProtoSink>());
            return probe;
        }));

        await using var host = builder.Build();
        await host.StartAsync();
        await host.StopAsync();

        var sink = probe!.Sinks.OfType<ConfigurableSink>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(sink.Configured, Is.EqualTo(new[] { "configured" }));
            Assert.That(sink.ExportCount, Is.EqualTo(1), "a directly registered sink is not registered twice");
        });
    }

    private sealed class StubInfrastructure(string id) : IProtoInfrastructure
    {
        public string Id { get; } = id;
        public string Kind => "stub";
        public string Description => "Stub infrastructure";
        public ProtoResourceScope Scope => ProtoResourceScope.Run;
        public int StartCount { get; private set; }
        public int ReleaseCount { get; private set; }

        public ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            StartCount++;
            return default;
        }

        public ValueTask ReleaseAsync(ProtoResourceReleaseContext context)
        {
            ReleaseCount++;
            return default;
        }
    }

    private sealed class StubConnectionInfrastructure(string id) : IProtoConnectionInfrastructure
    {
        public string Id { get; } = id;
        public string Kind => "stub";
        public string Description => "Stub infrastructure";
        public ProtoResourceScope Scope => ProtoResourceScope.Run;
        public string ConnectionString => "stub://connection";

        public ValueTask StartAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask ReleaseAsync(ProtoResourceReleaseContext context) => ValueTask.CompletedTask;
    }

    private sealed class CountingSink : IProtoSink
    {
        public int ExportCount { get; private set; }

        public Task ExportAsync(IEnumerable<ProtoReportItem> items, CancellationToken cancellationToken = default)
        {
            ExportCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class ConfigurableSink : IProtoSink
    {
        public List<string> Configured { get; } = [];

        public int ExportCount { get; private set; }

        public Task ExportAsync(IEnumerable<ProtoReportItem> items, CancellationToken cancellationToken = default)
        {
            ExportCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class SinkProbe(IEnumerable<IProtoSink> sinks) : IProtoRunHook
    {
        public IReadOnlyList<IProtoSink> Sinks { get; } = [.. sinks];
    }

    private sealed class SettingsProbe(ProtoInfrastructureSettings settings) : IProtoRunHook
    {
        public IReadOnlyDictionary<string, string> Values => settings.Values;
    }
}
