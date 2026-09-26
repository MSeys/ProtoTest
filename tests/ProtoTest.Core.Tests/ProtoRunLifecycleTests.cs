namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core.Internal;

/// <summary>
/// Contract of the extracted run starter (Audit 5 A5-15): it owns the start sequence and the unwind of a
/// failed start, while the host keeps the run state machine.
/// </summary>
[TestFixture]
public sealed class ProtoRunLifecycleTests
{
    [Test]
    public async Task StartAsync_ShouldStartInfrastructureAndRecordTheRunComposition()
    {
        var infrastructure = new TrackingInfrastructure("piece");
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(new ProtoInfrastructureSettings());
        services.AddSingleton(new ProtoInfrastructureRegistration(infrastructure, []));
        services.AddSingleton(new ProtoCapabilityDescriptor("Stub", "protocol", "Tests"));
        await using var provider = services.BuildServiceProvider();
        var trace = new ProtoTraceSession();
        var lifecycle = new ProtoRunLifecycle(provider, [], trace, new ProtoClock());

        await lifecycle.StartAsync(CancellationToken.None);

        var snapshot = trace.Snapshot();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(infrastructure.StartCount, Is.EqualTo(1), "registered infrastructure starts here");
            Assert.That(
                snapshot.Entities!.Any(entity => entity.Kind == ProtoTraceEntityKinds.Capability
                    && entity.Name == "Stub"),
                Is.True,
                "the starter records the run's capabilities");
        }
    }

    [Test]
    public async Task RollbackAsync_ShouldUnwindTheCompletedHooksInReverse()
    {
        var events = new List<string>();
        var first = new TrackingRunHook("first", events);
        var second = new TrackingRunHook("second", events);
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(new ProtoInfrastructureSettings());
        services.AddSingleton(new ProtoInfrastructureRegistration(
            new FailingInfrastructure("failing"), []));
        await using var provider = services.BuildServiceProvider();
        var lifecycle = new ProtoRunLifecycle(
            provider, [first, second], new ProtoTraceSession(), new ProtoClock());

        var startFailure = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await lifecycle.StartAsync(CancellationToken.None));
        var failures = await lifecycle.RollbackAsync(startFailure!, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                events,
                Is.EqualTo(new[] { "first:before", "second:before", "second:after", "first:after" }),
                "completed hooks unwind in reverse");
            Assert.That(
                failures,
                Has.Count.EqualTo(1),
                "a successful unwind leaves the original start failure alone");
        }
    }

    private sealed class TrackingInfrastructure(string id) : IProtoInfrastructure
    {
        public string Id => id;

        public string Kind => "tracked";

        public string Description => $"Tracked {id}";

        public ProtoResourceScope Scope => ProtoResourceScope.Run;

        public int StartCount { get; private set; }

        public ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            StartCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask ReleaseAsync(ProtoResourceReleaseContext context) => ValueTask.CompletedTask;
    }

    private sealed class FailingInfrastructure(string id) : IProtoInfrastructure
    {
        public string Id => id;

        public string Kind => "tracked";

        public string Description => $"Failing {id}";

        public ProtoResourceScope Scope => ProtoResourceScope.Run;

        public ValueTask StartAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromException(new InvalidOperationException($"{Id} failed to start."));

        public ValueTask ReleaseAsync(ProtoResourceReleaseContext context) => ValueTask.CompletedTask;
    }

    private sealed class TrackingRunHook(string name, List<string> events) : IProtoRunHook
    {
        public Task BeforeRunAsync(CancellationToken cancellationToken = default)
        {
            events.Add($"{name}:before");
            return Task.CompletedTask;
        }

        public Task AfterRunAsync(CancellationToken cancellationToken = default)
        {
            events.Add($"{name}:after");
            return Task.CompletedTask;
        }
    }
}
