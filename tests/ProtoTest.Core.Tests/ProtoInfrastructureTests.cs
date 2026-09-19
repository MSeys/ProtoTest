namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.DependencyInjection;
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

    [Test]
    public async Task InfrastructureSettings_ShouldBeClearedWhenTheRunStops()
    {
        var holder = CreateProbeBuilder(out var builder, new ClearingInfrastructure("thing:stop"), "ConnectionStrings:Thing");
        await using var host = builder.Build();
        await host.StartAsync();
        Assert.That(holder.Probe!.Values.Keys, Does.Contain("ConnectionStrings:Thing"));

        await host.StopAsync();

        Assert.That(holder.Probe.Values.Keys, Does.Not.Contain("ConnectionStrings:Thing"));
    }

    [Test]
    public async Task InfrastructureSettings_ShouldBeClearedWhenStartFails()
    {
        var holder = CreateProbeBuilder(
            out var builder, new ClearingInfrastructure("thing:ok"), "ConnectionStrings:Thing");
        builder.AddInfrastructure(new FailingInfrastructure("thing:fail"));
        await using var host = builder.Build();

        Assert.ThrowsAsync<InvalidOperationException>(async () => await host.StartAsync());

        Assert.That(
            holder.Probe!.Values,
            Is.Empty,
            "a released instance's connection string must not survive the rollback");
    }

    private static ProbeHolder CreateProbeBuilder(
        out ProtoHostBuilder builder,
        IProtoInfrastructure infrastructure,
        params string[] keys)
    {
        var holder = new ProbeHolder();
        var local = new ProtoHostBuilder();
        local.ConfigureTracing(options => options.Enabled = false);
        local.AddInfrastructure(infrastructure, keys);
        local.ConfigureServices(services => services.AddSingleton<IProtoRunHook>(provider =>
        {
            holder.Probe = new SettingsProbe(provider.GetRequiredService<ProtoInfrastructureSettings>());
            return holder.Probe;
        }));
        builder = local;
        return holder;
    }

    private sealed class ProbeHolder
    {
        public SettingsProbe? Probe { get; set; }
    }

    private sealed class ClearingInfrastructure(string id) : IProtoConnectionInfrastructure
    {
        public string Id => id;

        public string Kind => "thing";

        public string Description => "Fake thing";

        public ProtoResourceScope Scope => ProtoResourceScope.Run;

        public string ConnectionString => "fake://connection";

        public ValueTask StartAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask ReleaseAsync(ProtoResourceReleaseContext context) => ValueTask.CompletedTask;
    }

    private sealed class FailingInfrastructure(string id) : IProtoInfrastructure
    {
        public string Id => id;

        public string Kind => "thing";

        public string Description => "Failing thing";

        public ProtoResourceScope Scope => ProtoResourceScope.Run;

        public ValueTask StartAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromException(new InvalidOperationException("The failing thing did not start."));

        public ValueTask ReleaseAsync(ProtoResourceReleaseContext context) => ValueTask.CompletedTask;
    }

    private sealed class SettingsProbe(ProtoInfrastructureSettings settings) : IProtoRunHook
    {
        public IReadOnlyDictionary<string, string> Values => settings.Values;
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
