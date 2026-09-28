namespace ProtoTest.Testcontainers.Tests;

using Microsoft.Extensions.Configuration;
using ProtoTest.Core;

/// <summary>
/// The container provider: <c>UseContainer</c> holds on the Docker probe, fills the target's declared
/// keys when it wins, and falls through to the next provider when the daemon does not answer.
/// </summary>
[TestFixture]
public sealed class ContainerProviderTests
{
    private const string StoreKey = "Store:Connection";

    [Test]
    public async Task UseContainer_ShouldWinOnlyWhileDockerIsAvailable()
    {
        var container = new FakeContainer("fake:container", "Host=container");
        var fallback = new FakeContainer("fake:fallback", "Host=fallback");
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddInfrastructure(
            "Store",
            chain => chain
                .UseContainer(container)
                .Use(new ProtoTargetProvider("fallback", fallback)),
            StoreKey);
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("container provider", "00001", TestMethods.Placeholder);

        var values = Proto.Context.TryService<ProtoInfrastructureSettings>()!.Values;
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var entries = host.Trace.Snapshot().Entries!;
        var resolved = entries.Single(entry => entry.Kind == ProtoTargetTrace.Resolved);
        var docker = DockerProbe.IsAvailable();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                resolved.Attributes["environment.provider"],
                docker ? Is.EqualTo($"container:{container.Id}") : Is.EqualTo("fallback"),
                "the probe decides which provider serves the target");
            Assert.That(container.StartCount, docker ? Is.EqualTo(1) : Is.Zero);
            Assert.That(fallback.StartCount, docker ? Is.Zero : Is.EqualTo(1));
            Assert.That(
                values[StoreKey],
                docker ? Is.EqualTo("Host=container") : Is.EqualTo("Host=fallback"),
                "the winner fills the target's declared key");
            if (!docker)
            {
                var skipped = entries.Single(entry => entry.Kind == ProtoTargetTrace.ProviderSkipped);
                Assert.That(skipped.Attributes["environment.provider"], Is.EqualTo($"container:{container.Id}"));
                Assert.That(skipped.Attributes["environment.reason"], Is.EqualTo("Docker is available"));
            }
        }
    }

    [Test]
    public async Task UseContainer_ShouldSkipTheContainerWhenTheTargetIsConfigured()
    {
        var container = new FakeContainer("fake:container", "Host=container");
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [StoreKey] = "Host=configured"
            }));
        builder.AddInfrastructure("Store", chain => chain.UseConfigured().UseContainer(container), StoreKey);
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StopAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(container.StartCount, Is.Zero, "the configured provider wins before the container is considered");
            Assert.That(container.ReleaseCount, Is.Zero, "a losing container is never owned");
            Assert.That(
                host.Trace.Snapshot().Entities!.Single(entity => entity.Id == container.Id).State["infrastructure.state"],
                Is.EqualTo("skipped"));
        }
    }

    [Test]
    public void DockerProbe_ShouldAnswerWithoutThrowing()
    {
        Assert.DoesNotThrow(() => DockerProbe.IsAvailable());
        Assert.That(DockerProbe.Condition.Describe(new ProtoProviderConditionContext(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            [])), Is.EqualTo("Docker is available"));
    }

    private sealed class FakeContainer(string id, string connectionString) : IProtoConnectionInfrastructure
    {
        public string Id => id;

        public string Kind => "database";

        public string Description => $"Fake container {id}";

        public ProtoResourceScope Scope => ProtoResourceScope.Run;

        public string ConnectionString => connectionString;

        public int StartCount { get; private set; }

        public int ReleaseCount { get; private set; }

        public ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            StartCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask ReleaseAsync(ProtoResourceReleaseContext context)
        {
            ReleaseCount++;
            return ValueTask.CompletedTask;
        }
    }
}
