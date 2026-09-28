namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.Configuration;

/// <summary>
/// A target's provider chain through the real host builder: the first provider whose condition holds
/// serves it, only that provider's piece starts and declares its capability, the others are recorded
/// skipped with the reason, and a target no provider can serve fails the build.
/// </summary>
[TestFixture]
public sealed class TargetProviderChainTests
{
    private const string StoreKey = "Store:Connection";

    [Test]
    public async Task ConfiguredProvider_ShouldWinOverTheFallbackAndSkipIt()
    {
        var fallback = new TrackingInfrastructure("database:fallback", "Host=fallback");
        var builder = NewBuilder(("Store:Connection", "Host=configured"));
        builder.AddInfrastructure(
            "Store",
            chain => chain
                .UseConfigured()
                .Use(new ProtoTargetProvider("fallback", fallback)
                {
                    Capabilities = [new ProtoCapabilityDescriptor("Fallback store", ProtoCapabilityKinds.Store, "Tests")]
                }),
            StoreKey);
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("target chain", "00001", TestMethods.Placeholder);

        var values = Proto.Context.TryService<ProtoInfrastructureSettings>()!.Values;
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var snapshot = host.Trace.Snapshot();
        var resolved = snapshot.Entries!.Single(entry => entry.Kind == ProtoTargetTrace.Resolved);
        var skipped = snapshot.Entries!.Single(entry => entry.Kind == ProtoTargetTrace.ProviderSkipped);
        var entity = snapshot.Entities!.Single(candidate => candidate.Id == "database:fallback");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(fallback.StartCount, Is.Zero, "the configured provider won, so the fallback piece never started");
            Assert.That(fallback.ReleaseCount, Is.Zero, "a skipped piece is not owned or released");
            Assert.That(values.ContainsKey(StoreKey), Is.False, "the fallback published nothing");
            Assert.That(resolved.Attributes["environment.target"], Is.EqualTo("Store"));
            Assert.That(resolved.Attributes["environment.keys"], Is.EqualTo(StoreKey));
            Assert.That(resolved.Attributes["environment.provider"], Is.EqualTo("configured"));
            Assert.That(skipped.Attributes["environment.provider"], Is.EqualTo("fallback"));
            Assert.That(
                skipped.Attributes["environment.reason"],
                Does.Contain("earlier provider 'configured'"),
                "the reason names the provider that already serves the target");
            Assert.That(entity.State["infrastructure.state"], Is.EqualTo("skipped"));
            Assert.That(entity.State["infrastructure.reason"], Is.EqualTo(skipped.Attributes["environment.reason"]));
            Assert.That(
                snapshot.Entries!.Any(entry => entry.Kind == "resource.owned"
                    && entry.Attributes.TryGetValue("resource.id", out var id)
                    && id == "database:fallback"),
                Is.False,
                "a skipped piece is never owned");
            Assert.That(
                host.HasCapability(ProtoCapabilityKinds.Store),
                Is.False,
                "a losing provider's capability stays absent");
        }
    }

    [Test]
    public async Task SelectedProvider_ShouldWinAndTheEarlierProviderKeepItsUnmetCondition()
    {
        var selected = new TrackingInfrastructure("database:selected", "Host=selected");
        var fallback = new TrackingInfrastructure("database:fallback", "Host=fallback");
        var builder = NewBuilder(("Selection:Enabled", "true"));
        builder.AddInfrastructure(
            "Store",
            chain => chain
                .UseConfigured()
                .Use(new ProtoTargetProvider(
                    "selected",
                    selected,
                    ProtoProviderConditions.Selected("Selection:Enabled")))
                .Use(new ProtoTargetProvider("fallback", fallback)),
            StoreKey);
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("target chain", "00001", TestMethods.Placeholder);

        var values = Proto.Context.TryService<ProtoInfrastructureSettings>()!.Values;
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var entries = host.Trace.Snapshot().Entries!;
        var resolved = entries.Single(entry => entry.Kind == ProtoTargetTrace.Resolved);
        var skipped = entries.Where(entry => entry.Kind == ProtoTargetTrace.ProviderSkipped).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(selected.StartCount, Is.EqualTo(1));
            Assert.That(fallback.StartCount, Is.Zero);
            Assert.That(values[StoreKey], Is.EqualTo("Host=selected"));
            Assert.That(resolved.Attributes["environment.provider"], Is.EqualTo("selected"));
            Assert.That(skipped, Has.Length.EqualTo(2));
            Assert.That(skipped[0].Attributes["environment.provider"], Is.EqualTo("configured"));
            Assert.That(skipped[0].Attributes["environment.reason"], Does.Contain($"missing: {StoreKey}"));
            Assert.That(skipped[1].Attributes["environment.provider"], Is.EqualTo("fallback"));
            Assert.That(
                resolved.Attributes["environment.skipped"],
                Does.Contain("configured:").And.Contain("fallback:"),
                "the resolution record carries every skipped provider with its reason");
        }
    }

    [Test]
    public async Task UnavailableProvider_ShouldFallThroughToTheFallbackAndDeclareItsCapability()
    {
        var docker = new TrackingInfrastructure("database:docker", "Host=docker");
        var fallback = new TrackingInfrastructure("database:fallback", "Host=fallback");
        var builder = NewBuilder();
        builder.AddInfrastructure(
            "Store",
            chain => chain
                .Use(new ProtoTargetProvider(
                    "docker",
                    docker,
                    ProtoProviderConditions.Available("Docker is available", () => false)))
                .Use(new ProtoTargetProvider("fallback", fallback)
                {
                    Capabilities = [new ProtoCapabilityDescriptor("Fallback store", ProtoCapabilityKinds.Store, "Tests")]
                }),
            StoreKey);
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("target chain", "00001", TestMethods.Placeholder);

        var values = Proto.Context.TryService<ProtoInfrastructureSettings>()!.Values;
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var entries = host.Trace.Snapshot().Entries!;
        var resolved = entries.Single(entry => entry.Kind == ProtoTargetTrace.Resolved);
        var skipped = entries.Single(entry => entry.Kind == ProtoTargetTrace.ProviderSkipped);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(docker.StartCount, Is.Zero);
            Assert.That(fallback.StartCount, Is.EqualTo(1));
            Assert.That(values[StoreKey], Is.EqualTo("Host=fallback"));
            Assert.That(resolved.Attributes["environment.provider"], Is.EqualTo("fallback"));
            Assert.That(skipped.Attributes["environment.provider"], Is.EqualTo("docker"));
            Assert.That(skipped.Attributes["environment.reason"], Is.EqualTo("Docker is available"));
            Assert.That(
                host.HasCapability(ProtoCapabilityKinds.Store),
                Is.True,
                "the winner's capability is declared");
        }
    }

    [Test]
    public void NoProviderAvailable_ShouldFailTheBuildNamingEveryUnmetCondition()
    {
        var builder = NewBuilder();
        builder.AddInfrastructure(
            "Store",
            chain => chain
                .UseConfigured()
                .Use(new ProtoTargetProvider(
                    "docker",
                    Condition: ProtoProviderConditions.Available("Docker is available", () => false))),
            StoreKey);
        var exception = Assert.Throws<ProtoTargetResolutionException>(() => builder.Build());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.TargetName, Is.EqualTo("Store"));
            Assert.That(exception.Message, Does.Contain($"missing: {StoreKey}"));
            Assert.That(exception.Message, Does.Contain("Docker is available"));
        }
    }

    [Test]
    public async Task SharedProviderPiece_ShouldStartWhenAnyTargetItServesWinsIt()
    {
        var shared = new TrackingInfrastructure("database:shared", "Host=shared");
        var builder = NewBuilder(("A:Connection", "Host=configured"));
        builder.AddInfrastructure(
            "A",
            chain => chain
                .UseConfigured()
                .Use(new ProtoTargetProvider("shared", shared)),
            "A:Connection");
        builder.AddInfrastructure(
            "B",
            chain => chain.Use(new ProtoTargetProvider("shared", shared)),
            "B:Connection");
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("target chain", "00001", TestMethods.Placeholder);

        var values = Proto.Context.TryService<ProtoInfrastructureSettings>()!.Values;
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var resolutions = host.Trace.Snapshot().Entries!
            .Where(entry => entry.Kind == ProtoTargetTrace.Resolved)
            .ToDictionary(entry => entry.Attributes["environment.target"]!, entry => entry);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(shared.StartCount, Is.EqualTo(1), "one piece serves the target that won it");
            Assert.That(resolutions["A"].Attributes["environment.provider"], Is.EqualTo("configured"));
            Assert.That(resolutions["B"].Attributes["environment.provider"], Is.EqualTo("shared"));
            Assert.That(values["B:Connection"], Is.EqualTo("Host=shared"));
        }
    }

    [Test]
    public void RegisteringOneTargetTwice_ShouldFail()
    {
        var builder = NewBuilder();
        builder.AddInfrastructure("Store", chain => chain.UseConfigured(), StoreKey);

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.AddInfrastructure("Store", chain => chain.UseConfigured(), StoreKey));

        Assert.That(exception!.Message, Does.Contain("A target named 'Store' is already registered"));
    }

    [Test]
    public async Task ResolveAfter_ShouldResolveTheDependentChainAgainstTheEarlierWinner()
    {
        // The dependent chain registers first and still sees the application's winner: ResolveAfter
        // orders the resolution, not the registration.
        var observed = new TargetReadingCondition("Application");
        var builder = NewBuilder();
        builder.AddInfrastructure(
            "Worker",
            chain => chain
                .ResolveAfter("Application")
                .Use(new ProtoTargetProvider("host", Condition: observed)));
        builder.AddInfrastructure(
            "Application",
            chain => chain.Use(new ProtoTargetProvider("in-process")
            {
                Capabilities = [new ProtoCapabilityDescriptor("Server", ProtoCapabilityKinds.Server, "Tests")]
            }));
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(observed.Seen, Is.Not.Null, "the dependent chain's condition read the earlier target");
            Assert.That(observed.Seen!.TargetName, Is.EqualTo("Application"));
            Assert.That(observed.Seen.ProviderName, Is.EqualTo("in-process"));
            Assert.That(observed.Seen.HasCapability(ProtoCapabilityKinds.Server), Is.True);
        });
    }

    [Test]
    public async Task AddCapabilityWhenInProcess_ShouldKeepTheCapabilityWhileTheWinnerDeclaresAServer()
    {
        var builder = NewBuilder();
        AddAspNetCoreLikeTarget(builder, "Api", withServer: true);
        builder.AddCapabilityWhenInProcess("Api", InProcessCapability, "ProtoTest:Applications:Api:BaseUrl");
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StopAsync();

        Assert.That(
            host.HasCapability(ProtoCapabilityKinds.Device, "InProcessWebSocket"),
            Is.True,
            "the chain's winner serves the application in-process");
    }

    [Test]
    public async Task AddCapabilityWhenInProcess_ShouldDropTheCapabilityAndNameTheWinner()
    {
        var builder = NewBuilder();
        AddAspNetCoreLikeTarget(builder, "Api", withServer: false);
        builder.AddCapabilityWhenInProcess("Api", InProcessCapability, "ProtoTest:Applications:Api:BaseUrl");
        await using var host = builder.Build();
        await host.StartAsync();

        var skipped = host.Trace.Snapshot().Entries!.Single(entry => entry.Kind == "capability.skipped");
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(host.HasCapability(ProtoCapabilityKinds.Device, "InProcessWebSocket"), Is.False);
            Assert.That(skipped.Attributes["capability.reason"], Does.Contain("'external'"));
            Assert.That(skipped.Attributes["capability.reason"], Does.Contain("does not run it in-process"));
        });
    }

    [Test]
    public async Task AddCapabilityWhenInProcess_WithoutAChain_ShouldKeepTheConfiguredRule()
    {
        var builder = NewBuilder(("ProtoTest:Applications:Api:BaseUrl", "http://127.0.0.1:9"));
        builder.AddCapabilityWhenInProcess("Api", InProcessCapability, "ProtoTest:Applications:Api:BaseUrl");
        await using var host = builder.Build();
        await host.StartAsync();

        var skipped = host.Trace.Snapshot().Entries!.Single(entry => entry.Kind == "capability.skipped");
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(host.HasCapability(ProtoCapabilityKinds.Device, "InProcessWebSocket"), Is.False);
            Assert.That(skipped.Attributes["capability.reason"], Is.EqualTo("already configured"));
            Assert.That(skipped.Attributes["capability.keys"], Does.Contain("ProtoTest:Applications:Api:BaseUrl"));
        });
    }

    private static ProtoCapabilityDescriptor InProcessCapability { get; } = new(
        "InProcessWebSocket",
        ProtoCapabilityKinds.Device,
        "Tests")
    {
        Instance = "Api"
    };

    private static void AddAspNetCoreLikeTarget(ProtoHostBuilder builder, string application, bool withServer)
    {
        var capabilities = withServer
            ? new[] { new ProtoCapabilityDescriptor("ASP.NET Core", ProtoCapabilityKinds.Server, "Tests") }
            : [];
        builder.AddInfrastructure(
            application,
            chain => chain.Use(new ProtoTargetProvider(withServer ? "in-process" : "external")
            {
                Capabilities = capabilities
            }),
            $"ProtoTest:Applications:{application}:BaseUrl");
    }

    private sealed class TargetReadingCondition(string targetName) : IProtoProviderCondition
    {
        public ProtoResolvedTarget? Seen { get; private set; }

        public bool IsSatisfied(ProtoProviderConditionContext context)
        {
            Seen = context.Target(targetName);
            return Seen is not null;
        }

        public string Describe(ProtoProviderConditionContext context)
            => $"The target '{targetName}' must resolve first";
    }

    private static ProtoHostBuilder NewBuilder(params (string Key, string Value)[] configuration)
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        if (configuration.Length > 0)
        {
            builder.ConfigureAppConfiguration(sources => sources.AddInMemoryCollection(
                configuration.ToDictionary(pair => pair.Key, pair => (string?)pair.Value)));
        }

        return builder;
    }

    private sealed class TrackingInfrastructure(string id, string connectionString) : IProtoConnectionInfrastructure
    {
        public string Id => id;

        public string Kind => "database";

        public string Description => $"Tracked {Id}";

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
}
