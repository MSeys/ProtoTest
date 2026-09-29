namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.Configuration;

// Pins the obsolete AddInfrastructure skip-key surface, the 1.x compatibility path; the chain
// replacement is covered by TargetProviderChainTests. CS0618 is expected here.
#pragma warning disable CS0618

[TestFixture]
public sealed class ConditionalInfrastructureTests
{
    [Test]
    public async Task AddInfrastructure_WhenEveryKeyIsConfigured_ShouldSkipTheProvider()
    {
        // A skipped piece must leave no ownership record, and the record is only visible in the trace's
        // entities and entries, so the fixture reads a snapshot rather than a return value.
        using var trace = new TemporaryTrace("skip");
        var provider = new TrackingConnectionInfrastructure("database:tracked", "Host=container");
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.OutputPath = trace.Path);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:App"] = "Host=configured"
            }));
        builder.AddInfrastructure(provider, "ConnectionStrings:App");
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("skipped", "00001", TestMethods.Placeholder);

        var values = Proto.Context.TryService<ProtoInfrastructureSettings>()!.Values;
        var configured = Proto.Context.Configuration["ConnectionStrings:App"];
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var snapshot = host.Trace.Snapshot();
        var entity = snapshot.Entities!.Single(candidate => candidate.Id == "database:tracked");
        Assert.Multiple(() =>
        {
            Assert.That(provider.StartCount, Is.Zero, "the configured environment does not need the piece");
            Assert.That(provider.ReleaseCount, Is.Zero, "a piece that never started is not released");
            Assert.That(values.ContainsKey("ConnectionStrings:App"), Is.False, "the piece fills nothing");
            Assert.That(configured, Is.EqualTo("Host=configured"));
            Assert.That(entity.State["infrastructure.state"], Is.EqualTo("skipped"));
            Assert.That(entity.State["infrastructure.reason"], Is.EqualTo("already configured"));
            Assert.That(
                snapshot.Entries!.Any(entry => entry.Kind == "resource.owned"),
                Is.False,
                "a skipped piece is not recorded as owned");
        });
    }

    [Test]
    public async Task AddInfrastructure_WhenAKeyIsMissing_ShouldStartAndFill()
    {
        var provider = new TrackingConnectionInfrastructure("database:tracked", "Host=container");
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddInfrastructure(provider, "ConnectionStrings:App");
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("started", "00001", TestMethods.Placeholder);

        var values = Proto.Context.TryService<ProtoInfrastructureSettings>()!.Values;
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(provider.StartCount, Is.EqualTo(1));
            Assert.That(values["ConnectionStrings:App"], Is.EqualTo("Host=container"));
        });
    }

    [Test]
    public async Task AddInfrastructure_WhenOnlySomeKeysAreConfigured_ShouldStillStart()
    {
        var provider = new TrackingConnectionInfrastructure("database:tracked", "Host=container");
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:App"] = "Host=configured"
            }));
        builder.AddInfrastructure(provider, "ConnectionStrings:App", "ConnectionStrings:Secondary");
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("partial", "00001", TestMethods.Placeholder);

        var values = Proto.Context.TryService<ProtoInfrastructureSettings>()!.Values;
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(provider.StartCount, Is.EqualTo(1), "a partially configured piece is still needed");
            Assert.That(values["ConnectionStrings:App"], Is.EqualTo("Host=container"));
            Assert.That(values["ConnectionStrings:Secondary"], Is.EqualTo("Host=container"));
        });
    }

    [Test]
    public async Task AddInfrastructureAlways_WhenConfigured_ShouldStartAnyway()
    {
        var provider = new TrackingConnectionInfrastructure("database:tracked", "Host=container");
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:App"] = "Host=configured"
            }));
        builder.AddInfrastructureAlways(provider, "ConnectionStrings:App");
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("always", "00001", TestMethods.Placeholder);

        var values = Proto.Context.TryService<ProtoInfrastructureSettings>()!.Values;
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(provider.StartCount, Is.EqualTo(1));
            Assert.That(values["ConnectionStrings:App"], Is.EqualTo("Host=container"));
        });
    }

    [Test]
    public async Task AddInfrastructure_WithASettingsProviderKey_ShouldSkipWhenConfigured()
    {
        var provider = new TrackingSettingsInfrastructure("standalone", "ProtoTest:Applications:Web:BaseUrl", "http://standalone");
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:Web:BaseUrl"] = "https://staging"
            }));
        builder.AddInfrastructure(provider, "ProtoTest:Applications:Web:BaseUrl");
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("settings provider", "00001", TestMethods.Placeholder);

        var values = Proto.Context.TryService<ProtoInfrastructureSettings>()!.Values;
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(provider.StartCount, Is.Zero);
            Assert.That(values.ContainsKey("ProtoTest:Applications:Web:BaseUrl"), Is.False, "the piece fills nothing");
        });
    }

    [Test]
    public async Task AddInfrastructure_RepeatedRegistration_ShouldKeepTheAlwaysFlag()
    {
        var provider = new TrackingConnectionInfrastructure("database:tracked", "Host=container");
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:App"] = "Host=configured"
            }));
        builder.AddInfrastructureAlways(provider, "ConnectionStrings:App");
        builder.AddInfrastructure(provider, "ConnectionStrings:Secondary");
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StopAsync();

        Assert.That(provider.StartCount, Is.EqualTo(1), "one always-call keeps the piece starting");
    }

    private sealed class TrackingConnectionInfrastructure(string id, string connectionString) : IProtoConnectionInfrastructure
    {
        public string Id => id;

        public string Kind => "database";

        public string Description => $"Tracked {id}";

        public ProtoResourceScope Scope => ProtoResourceScope.Run;

        public string ConnectionString { get; private set; } = string.Empty;

        public int StartCount { get; private set; }

        public int ReleaseCount { get; private set; }

        public ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            StartCount++;
            ConnectionString = connectionString;
            return ValueTask.CompletedTask;
        }

        public ValueTask ReleaseAsync(ProtoResourceReleaseContext context)
        {
            ReleaseCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TrackingSettingsInfrastructure : IProtoSettingsInfrastructure
    {
        private readonly string _key;
        private readonly string _value;

        public TrackingSettingsInfrastructure(string id, string key, string value)
        {
            Id = id;
            _key = key;
            _value = value;
            Settings = new Dictionary<string, string>();
        }

        public string Id { get; }

        public string Kind => "settings";

        public string Description => $"Tracked {Id}";

        public ProtoResourceScope Scope => ProtoResourceScope.Run;

        public IReadOnlyDictionary<string, string> Settings { get; private set; }

        public int StartCount { get; private set; }

        public ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            StartCount++;
            Settings = new Dictionary<string, string> { [_key] = _value };
            return ValueTask.CompletedTask;
        }

        public ValueTask ReleaseAsync(ProtoResourceReleaseContext context) => ValueTask.CompletedTask;
    }
}

#pragma warning restore CS0618
