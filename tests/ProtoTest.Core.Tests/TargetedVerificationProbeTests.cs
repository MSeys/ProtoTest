namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.DependencyInjection;

[TestFixture]
public sealed class TargetedVerificationProbeTests
{
    [Test]
    public async Task AddSink_ConfiguredTwiceAfterADirectRegistration_ShouldStayOneSink()
    {
        SinkProbe? probe = null;
        var builder = new ProtoHostBuilder();
        builder.ConfigureServices(services => services.AddSingleton<IProtoSink, ConfigurableSink>());
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

        var sinks = probe!.Sinks.OfType<ConfigurableSink>().ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(sinks, Has.Length.EqualTo(1), "a directly registered sink must stay one sink");
            Assert.That(sinks[0].Configured, Is.EqualTo(new[] { "first", "second" }));
            Assert.That(sinks[0].ExportCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task StopAsync_WhileInfrastructureIsStarting_ShouldBeRejected()
    {
        var infrastructure = new BlockingInfrastructure("blocking");
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddInfrastructure(infrastructure);
        var host = builder.Build();

        var start = host.StartAsync();
        await infrastructure.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await host.StopAsync());
        Assert.That(exception!.Message, Does.Contain("in progress"));

        infrastructure.Release.TrySetResult();
        await start;

        // The start that completed normally after the rejected stop is still a live run.
        await host.StopAsync();
        await host.DisposeAsync();
    }

    [Test]
    public async Task DisposeAsync_WhileInfrastructureIsStarting_ShouldBeRejected()
    {
        var infrastructure = new BlockingInfrastructure("blocking");
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddInfrastructure(infrastructure);
        var host = builder.Build();

        var start = host.StartAsync();
        await infrastructure.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await host.DisposeAsync());
        Assert.That(exception!.Message, Does.Contain("in progress"));

        infrastructure.Release.TrySetResult();
        await start;

        await host.StopAsync();
        await host.DisposeAsync();
    }

    private sealed class BlockingInfrastructure(string id) : IProtoInfrastructure
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Id { get; } = id;
        public string Kind => "blocking";
        public string Description => "Blocking infrastructure";
        public ProtoResourceScope Scope => ProtoResourceScope.Run;

        public async ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }

        public ValueTask ReleaseAsync(ProtoResourceReleaseContext context) => ValueTask.CompletedTask;
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
}
