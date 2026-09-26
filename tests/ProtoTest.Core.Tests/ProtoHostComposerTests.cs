namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core.Internal;

/// <summary>
/// Contract of the build-time composer: one pass resolves the conditional
/// decisions from the collection and registers the run's services, and the readiness options register
/// through the shared options registrar.
/// </summary>
[TestFixture]
public sealed class ProtoHostComposerTests
{
    private const string ConfiguredKey = "ConnectionStrings:Configured";

    [Test]
    public void Compose_ShouldResolveTheConditionalDecisionsAndRegisterTheRunServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ConfiguredKey] = "Host=configured"
            })
            .Build();
        var infrastructure = new StubInfrastructure("database:composed");
        var capability = new ProtoCapabilityDescriptor("Adapter", "protocol", "Tests");
        var runResources = new ProtoRunResourceStore();
        runResources.Add(infrastructure);
        var services = new ServiceCollection();
        services.AddSingleton(new ProtoInfrastructureRegistration(infrastructure, [ConfiguredKey]));
        services.AddSingleton(capability);
        services.AddSingleton(new ProtoConditionalCapability(capability, new ProtoKeySet([ConfiguredKey])));
        var composer = new ProtoHostComposer(
            services,
            configuration,
            new ProtoTestIdOptions(),
            new ProtoTraceOptions(),
            runResources,
            new ProtoReadinessOptions());

        composer.Compose();

        using var provider = services.BuildServiceProvider();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                provider.GetRequiredService<ProtoSkippedInfrastructure>().Ids,
                Does.Contain("database:composed"),
                "the configured piece is skipped, not started");
            Assert.That(runResources.HasResources, Is.False, "a skipped piece is not owned");
            Assert.That(
                provider.GetRequiredService<ProtoSkippedCapabilities>().Capabilities,
                Has.Count.EqualTo(1),
                "the capability no declaration needs is dropped");
            Assert.That(
                provider.GetService<ProtoCapabilityDescriptor>(),
                Is.Null,
                "a dropped capability leaves the run overview");
            Assert.That(provider.GetRequiredService<ProtoTraceSession>(), Is.Not.Null);
            Assert.That(provider.GetRequiredService<ProtoClock>(), Is.Not.Null);
        }
    }

    [Test]
    public void Compose_ShouldBindReadinessConfigurationOverTheCodeValues()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProtoTest:Readiness:Interval"] = "00:00:00.250"
            })
            .Build();
        var readiness = new ProtoReadinessOptions { Interval = TimeSpan.FromMilliseconds(25) };
        var services = new ServiceCollection();
        var composer = new ProtoHostComposer(
            services,
            configuration,
            new ProtoTestIdOptions(),
            new ProtoTraceOptions(),
            new ProtoRunResourceStore(),
            readiness);

        composer.Compose();

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<ProtoReadinessOptions>();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                resolved,
                Is.SameAs(readiness),
                "the probes' instance is the one the registrar resolves, so one policy governs every wait");
            Assert.That(
                resolved.Interval,
                Is.EqualTo(TimeSpan.FromMilliseconds(250)),
                "ProtoTest:Readiness binds over the code value");
        }
    }

    private sealed class StubInfrastructure(string id) : IProtoInfrastructure
    {
        public string Id => id;

        public string Kind => "database";

        public string Description => $"Stub {id}";

        public ProtoResourceScope Scope => ProtoResourceScope.Run;

        public ValueTask StartAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask ReleaseAsync(ProtoResourceReleaseContext context) => ValueTask.CompletedTask;
    }
}
