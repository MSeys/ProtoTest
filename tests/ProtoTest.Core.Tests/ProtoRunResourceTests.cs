namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

[TestFixture]
public class ProtoRunResourceTests
{
    [Test]
    public async Task RunResource_ShouldBeReleasedWhenTheRunStops()
    {
        // Arrange
        var released = new List<string>();
        var builder = new ProtoHostBuilder();
        builder.AddResource(new ProtoResource(
            "database:container",
            "database",
            "Postgres container",
            _ =>
            {
                released.Add("database:container");
                return ValueTask.CompletedTask;
            },
            ProtoResourceScope.Run));
        await using var host = builder.Build();

        // Act
        await host.StartAsync();
        await host.StopAsync();

        // Assert: released once the gates and the report sinks have had their turn.
        Assert.That(released, Is.EqualTo(new[] { "database:container" }));
    }

    [Test]
    public async Task RunResource_ShouldStillBeReleasedWhenTheHostIsDisposedWithoutStopping()
    {
        // Arrange
        var released = new List<string>();
        var builder = new ProtoHostBuilder();
        builder.AddResource(new ProtoResource(
            "database:container",
            "database",
            "Postgres container",
            _ =>
            {
                released.Add("database:container");
                return ValueTask.CompletedTask;
            },
            ProtoResourceScope.Run));
        var host = builder.Build();
        await host.StartAsync();

        // Act: `await using` alone must not leak the resource.
        await host.DisposeAsync();

        // Assert
        Assert.That(released, Is.EqualTo(new[] { "database:container" }));
    }

    [Test]
    public async Task RunResource_ShouldSurfaceReleaseFailures()
    {
        // Arrange
        var builder = new ProtoHostBuilder();
        builder.AddResource(new ProtoResource(
            "broken",
            "database",
            "Broken container",
            _ => throw new InvalidOperationException("The container refused to stop."),
            ProtoResourceScope.Run));
        var host = builder.Build();
        await host.StartAsync();

        // Act
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await host.DisposeAsync());

        // Assert
        Assert.That(exception!.Message, Is.EqualTo("The container refused to stop."));
    }

    [Test]
    public async Task RunResources_ShouldKeepReleasingAfterOneFails()
    {
        // Arrange
        var released = new List<string>();
        var builder = new ProtoHostBuilder();
        builder.AddResource(new ProtoResource(
            "broken", "database", "Broken container",
            _ => throw new InvalidOperationException("boom"), ProtoResourceScope.Run));
        builder.AddResource(new ProtoResource(
            "healthy", "database", "Healthy container",
            _ =>
            {
                released.Add("healthy");
                return ValueTask.CompletedTask;
            },
            ProtoResourceScope.Run));
        var host = builder.Build();
        await host.StartAsync();

        // Act: two failures would be needed for an aggregate, one is rethrown as itself.
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await host.DisposeAsync());

        // Assert
        Assert.That(exception!.Message, Is.EqualTo("boom"));
        Assert.That(released, Is.EqualTo(new[] { "healthy" }));
    }

    [Test]
    public async Task RunResources_ShouldBeRecordedInTheRunTrace()
    {
        // Arrange
        var builder = new ProtoHostBuilder();
        builder.AddResource(new ProtoResource(
            "database:container", "database", "Postgres container", _ => default, ProtoResourceScope.Run));
        await using var host = builder.Build();

        // Act
        await host.StartAsync();
        await host.StopAsync();

        // Assert: both the ownership and the release belong to the run, not to a test.
        var entries = host.Trace.Snapshot().Entries;
        Assert.That(entries, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entries!.Select(entry => entry.Kind), Does.Contain("resource.owned"));
            var release = entries!.Single(entry => entry.Kind == "resource.release");
            Assert.That(release.Outcome, Is.EqualTo(ProtoTraceOutcome.Succeeded));
            Assert.That(release.Phase, Is.EqualTo(ProtoTracePhase.Run));
            Assert.That(release.Attributes["resource.kind"], Is.EqualTo("database"));
        }
    }

    [Test]
    public void AddResource_ShouldRejectTestScopedResources()
    {
        // Arrange
        var builder = new ProtoHostBuilder();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => builder.AddResource(
            new ProtoResource("client", "client", "A test-scoped client", _ => default)));
        Assert.That(exception!.Message, Does.Contain("only run-scoped resources belong to the host"));
    }

    [Test]
    public void RegisterResource_ShouldRejectRunScopedResources()
    {
        // Arrange
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = new ProtoExecutionContext("Test", scope, "00001", (MethodInfo)MethodInfo.GetCurrentMethod()!);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => context.RegisterResource(
            new ProtoResource("container", "database", "Container", _ => default, ProtoResourceScope.Run)));
        Assert.That(exception!.Message, Does.Contain("AddResource"));
    }

    [Test]
    public async Task DisposeAsync_ShouldRunAfterRunHooksBeforeReleasingRunResourcesAndCompleteTheTraceLast()
    {
        // The trace's completion has no callback a test can attach to, so it is sampled through
        // Snapshot().CompletedAtUtc: the run is still open while the hook and the release run, and
        // completed once `await using` returns. The ordering decision under test is hook -> release.
        var events = new List<string>();
        IProtoTraceSource? trace = null;
        var builder = new ProtoHostBuilder();
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IProtoRunHook>(new OrderRecordingRunHook(events, () => trace));
            services.AddSingleton<IProtoReportSource>(new OrderRecordingReportSource(events));
        });
        builder.AddRunGate("order probe", _ => ProtoRunGateResult.Passed());
        builder.AddResource(new ProtoResource(
            "order-probe",
            "probe",
            "Records when run-scoped resources are released",
            _ =>
            {
                events.Add($"resource:release (trace completed: {trace!.Snapshot().CompletedAtUtc is not null})");
                return ValueTask.CompletedTask;
            },
            ProtoResourceScope.Run));

        await using (var host = builder.Build())
        {
            trace = host.Trace;
            await host.StartAsync();
        }

        Assert.Multiple(() =>
        {
            Assert.That(events, Is.EqualTo(new[]
            {
                "hook:after (trace completed: False)",
                "report:collect",
                "resource:release (trace completed: False)"
            }));
            Assert.That(trace!.Snapshot().CompletedAtUtc, Is.Not.Null);
        });
    }

    private sealed class OrderRecordingRunHook(List<string> events, Func<IProtoTraceSource?> trace) : IProtoRunHook
    {
        public Task AfterRunAsync(CancellationToken cancellationToken = default)
        {
            var completed = trace()?.Snapshot().CompletedAtUtc is not null;
            events.Add($"hook:after (trace completed: {completed})");
            return Task.CompletedTask;
        }
    }

    private sealed class OrderRecordingReportSource(List<string> events) : IProtoReportSource
    {
        public IEnumerable<ProtoReportItem> GetReportItems()
        {
            events.Add("report:collect");
            return [];
        }
    }
}
