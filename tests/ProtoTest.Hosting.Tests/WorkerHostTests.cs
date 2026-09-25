namespace ProtoTest.Hosting.Tests;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProtoTest.Core;
using ProtoTest.Hosting.TestWorker;

public sealed class WorkerHostTests
{
    private static readonly string WorkerAssembly = typeof(Program).Assembly.GetName().Name!;

    [Test]
    public async Task WorkerHost_ShouldStartWithTheRunServeTestsAndStopWithIt()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddWorkerHost<Program>("Billing");
        await using var host = builder.Build();
        await host.StartAsync();

        await host.StartTestAsync("worker host", "00001", TestMethods.Placeholder);
        var probe = Proto.Context.HostService<Program, WorkerProbe>("Billing");
        var resolved = Proto.Context.Host<Program>("Billing");
        var resolvedProbe = resolved.Services.GetRequiredService<WorkerProbe>();
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var entities = host.Trace.Snapshot().Entities!
            .Where(entity => entity.Kind == ProtoCapabilityKinds.Worker)
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(probe.Started, Is.True, "the worker's hosted service started with the run");
            Assert.That(probe.Stopped, Is.True, "the worker's hosted service stopped with the run");
            Assert.That(resolvedProbe, Is.SameAs(probe));
            Assert.That(host.HasCapability(ProtoCapabilityKinds.Worker, WorkerAssembly), Is.True);
            Assert.That(entities, Has.Length.EqualTo(1), "one worker entity is recorded");
            Assert.That(entities[0].Id, Is.EqualTo("Billing"));
        });
    }

    [Test]
    public async Task WorkerHost_ShouldReadTheRunsSettingsAndTheSuitesOptions()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Worker:Value"] = "from-config",
                ["Worker:FromConfig"] = "config-only"
            }));
        builder.AddInfrastructure(new FakeBroker(), "ConnectionStrings:WorkerProbe");
        builder.AddInfrastructure(new FakeSettings());
        builder.AddWorkerHost<Program>("Configured", options => options.Set("Worker:Value", "from-suite"));
        await using var host = builder.Build();
        await host.StartAsync();

        await host.StartTestAsync("configured worker", "00001", TestMethods.Placeholder);
        var probe = Proto.Context.HostService<Program, WorkerProbe>("Configured");
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(
                probe.ConnectionString,
                Is.EqualTo("amqp://probe"),
                "the worker reads what earlier infrastructure provided");
            Assert.That(probe.ConfigValue, Is.EqualTo("config-only"), "the suite's configuration reaches the worker");
            Assert.That(
                probe.Value,
                Is.EqualTo("from-suite"),
                "precedence is options over infrastructure settings over suite configuration");
        });
    }

    [Test]
    public async Task WorkerHost_ShouldReadTheRunsClock()
    {
        var seed = new DateTimeOffset(2026, 2, 2, 8, 0, 0, TimeSpan.Zero);
        var builder = new ProtoHostBuilder();
        builder.ConfigureClock(new ProtoClock(seed));
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddWorkerHost<Program>("Timed");
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("timed worker", "00001", TestMethods.Placeholder);
        var probe = Proto.Context.HostService<Program, WorkerProbe>("Timed");
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.That(
            probe.StartedAtUtc,
            Is.EqualTo(seed),
            "the worker's TimeProvider resolves the run clock on its background flow");
    }

    [Test]
    public async Task WorkerHost_WhenStartFails_ShouldFailTheRunWithTheWorkerNamed()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddWorkerHost<Program>("Failing", options => options.Set("Worker:FailStart", "true"));
        await using var host = builder.Build();

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await host.StartAsync());

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("'Failing'"), "the failure names the worker");
            Assert.That(exception.Message, Does.Contain("failed to start"));
            Assert.That(Flatten(exception), Does.Contain("told to fail at start"));
        });
    }

    [Test]
    public async Task WorkerHost_WhenStopFails_ShouldReportTheReleaseFailure()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddWorkerHost<Program>("Failing", options => options.Set("Worker:FailStop", "true"));
        await using var host = builder.Build();
        await host.StartAsync();

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await host.StopAsync());

        Assert.That(Flatten(exception!), Does.Contain("told to fail at stop"));
    }

    [Test]
    public async Task WorkerHost_WhenRegisteredTwiceForTheSameName_ShouldHostOneWorker()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddWorkerHost<Program>("Billing");
        builder.AddWorkerHost<Program>("Billing");
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("once", "00001", TestMethods.Placeholder);
        var probe = Proto.Context.HostService<Program, WorkerProbe>("Billing");
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var entities = host.Trace.Snapshot().Entities!.Count(entity => entity.Kind == ProtoCapabilityKinds.Worker);
        Assert.That(entities, Is.EqualTo(1), "the first registration owns the worker's lifecycle");
        Assert.That(probe.Started, Is.True);
    }

    [Test]
    public async Task HostAccessor_WhenSeveralWorkersHostTheSameProgram_ShouldAskForTheName()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddWorkerHost<Program>("First");
        builder.AddWorkerHost<Program>("Second");
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("two workers", "00001", TestMethods.Placeholder);

        var ambiguous = Assert.Throws<InvalidOperationException>(() => Proto.Context.Host<Program>());
        var first = Proto.Context.Host<Program>("First");
        var second = Proto.Context.Host<Program>("Second");

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(ambiguous!.Message, Does.Contain("Several worker hosts"));
            Assert.That(ambiguous.Message, Does.Contain("'First'").And.Contains("'Second'"));
            Assert.That(first, Is.Not.SameAs(second));
        });
    }

    [Test]
    public void AddWorkerHost_WhenTheProgramAssemblyIsALibrary_ShouldExplain()
    {
        var builder = new ProtoHostBuilder();

        // ProtoTest.Core is a library: it has no entry point to run, so no host factory can be resolved.
        var exception = Assert.Throws<InvalidOperationException>(() => builder.AddWorkerHost<ProtoHostBuilder>());

        Assert.That(exception!.Message, Does.Contain("No host factory could be resolved"));
    }

    [Test]
    public async Task HostAccessor_WithoutHosting_ShouldExplain()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("no workers", "00001", TestMethods.Placeholder);

        var exception = Assert.Throws<InvalidOperationException>(() => Proto.Context.HostService<Program, WorkerProbe>());

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.That(exception!.Message, Does.Contain("not built with ProtoTest.Hosting"));
    }

    [Test]
    public async Task HostAccessor_ForADifferentProgram_ShouldExplain()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddWorkerHost<Program>("Billing");
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("wrong program", "00001", TestMethods.Placeholder);

        var exception = Assert.Throws<InvalidOperationException>(() => Proto.Context.Host<Program>("Payments"));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.That(exception!.Message, Does.Contain("No worker host named 'Payments'"));
    }

    private static string Flatten(Exception exception)
    {
        var messages = new List<string>();
        var queue = new Queue<Exception>();
        queue.Enqueue(exception);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            messages.Add(current.Message);
            if (current.InnerException is not null)
            {
                queue.Enqueue(current.InnerException);
            }

            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    queue.Enqueue(inner);
                }
            }
        }

        return string.Join(" | ", messages);
    }

    private sealed class FakeBroker : IProtoConnectionInfrastructure
    {
        public string Id => "broker";

        public string Kind => "broker";

        public string Description => "Fake broker";

        public ProtoResourceScope Scope => ProtoResourceScope.Run;

        public string ConnectionString => "amqp://probe";

        public ValueTask StartAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask ReleaseAsync(ProtoResourceReleaseContext context) => ValueTask.CompletedTask;
    }

    private sealed class FakeSettings : IProtoSettingsInfrastructure
    {
        public string Id => "settings";

        public string Kind => "settings";

        public string Description => "Fake settings";

        public ProtoResourceScope Scope => ProtoResourceScope.Run;

        public IReadOnlyDictionary<string, string> Settings { get; } =
            new Dictionary<string, string> { ["Worker:Value"] = "from-run" };

        public ValueTask StartAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask ReleaseAsync(ProtoResourceReleaseContext context) => ValueTask.CompletedTask;
    }
}
