namespace ProtoTest.Core.Tests;

using NUnit.Framework;

[TestFixture]
public sealed class ProtoInfrastructureTests
{
    [Test]
    public async Task Infrastructure_ShouldStartWithTheHostAndRecordItsSettings()
    {
        var infrastructure = new FakeInfrastructure();
        var builder = new ProtoHostBuilder();
        builder.AddInfrastructure(infrastructure, "ConnectionStrings:Thing", "ProtoTest:Thing:Connection");
        await using var host = builder.Build();
        Assert.That(infrastructure.Started, Is.False, "Infrastructure starts with the run, not with the builder.");

        await host.StartAsync();

        var entity = host.Trace.Snapshot().Entities!.Single(candidate => candidate.Id == "thing:fake");
        Assert.Multiple(() =>
        {
            Assert.That(infrastructure.Started, Is.True);
            Assert.That(entity.State["infrastructure.kind"], Is.EqualTo("thing"));
            Assert.That(entity.State["infrastructure.settings"], Does.Contain("ConnectionStrings:Thing"));
        });
    }

    [Test]
    public void Infrastructure_WithoutAConnectionString_CannotFillSettings()
    {
        var builder = new ProtoHostBuilder();
        Assert.Throws<ArgumentException>(() =>
            builder.AddInfrastructure(new PlainInfrastructure(), "ConnectionStrings:Thing"));
    }

    private sealed class FakeInfrastructure : IProtoConnectionInfrastructure
    {
        public string Id => "thing:fake";

        public string Kind => "thing";

        public string Description => "Fake thing";

        public ProtoResourceScope Scope => ProtoResourceScope.Run;

        public string ConnectionString => "fake://connection";

        public bool Started { get; private set; }

        public ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            Started = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask ReleaseAsync(ProtoResourceReleaseContext context) => ValueTask.CompletedTask;
    }

    private sealed class PlainInfrastructure : IProtoInfrastructure
    {
        public string Id => "plain";

        public string Kind => "plain";

        public string Description => "Plain";

        public ProtoResourceScope Scope => ProtoResourceScope.Run;

        public ValueTask StartAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask ReleaseAsync(ProtoResourceReleaseContext context) => ValueTask.CompletedTask;
    }
}
