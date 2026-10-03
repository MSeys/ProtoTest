namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// <see cref="IProtoRunHook.AfterInfrastructureAsync"/> through the host: it runs once every piece
/// started and before the first test, reaches the run's applications, and a failure fails the start.
/// </summary>
[TestFixture]
public sealed class RunAfterInfrastructureTests
{
    private const string Key = "ConnectionStrings:Thing";

    [Test]
    public async Task AfterInfrastructure_ShouldRunAfterEveryPieceStartedWithItsSettings()
    {
        var events = new List<string>();
        var hook = new RecordingHook(events);
        var builder = Builder(events);
        builder.ConfigureServices(services => services.AddSingleton<IProtoRunHook>(hook));
        await using var host = builder.Build();
        using var cancellation = new CancellationTokenSource();

        await host.StartAsync(cancellation.Token);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(events, Is.EqualTo(new[] { "before", "infrastructure", "after-infrastructure" }));
            Assert.That(hook.Context!.Settings.Values[Key], Is.EqualTo("fake://connection"), "the hook sees what the pieces published");
            Assert.That(hook.Context.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(hook.Context.Services.GetService<ProtoInfrastructureSettings>(), Is.Not.Null, "the hook reaches the run's services");
        }
    }

    [Test]
    public async Task AfterInfrastructure_ShouldFailTheStartAndReleaseWhatStarted()
    {
        var events = new List<string>();
        var hook = new RecordingHook(events) { FailOnce = true };
        var builder = Builder(events);
        builder.ConfigureServices(services => services.AddSingleton<IProtoRunHook>(hook));
        await using var host = builder.Build();

        var failure = Assert.ThrowsAsync<InvalidOperationException>(async () => await host.StartAsync());
        var afterFailure = events.ToArray();
        await host.StartAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure!.Message, Is.EqualTo("The hook failed."));
            Assert.That(afterFailure, Does.Contain("released"), "the started piece is released");
            Assert.That(afterFailure, Does.Contain("after-run"), "the hook unwinds like a failed BeforeRun");
            Assert.That(events.Count(entry => entry == "after-infrastructure"), Is.EqualTo(2), "a retry runs the phase again");
        }
    }

    [Test]
    public async Task ApplicationClient_ShouldUseTheConfiguredAddressAndReleaseItWithThePhase()
    {
        var hook = new ClientHook("Api");
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["ProtoTest:Applications:Api:BaseUrl"] = "http://api.test/" }));
        builder.ConfigureServices(services => services.AddSingleton<IProtoRunHook>(hook));
        await using var host = builder.Build();

        await host.StartAsync();

        var opened = host.Trace.Snapshot().Entries!.Single(entry => entry.Kind == "run.application.client");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hook.Client!.BaseAddress, Is.EqualTo(new Uri("http://api.test/")));
            Assert.That(opened.Attributes["application.mode"], Is.EqualTo("address"));
            Assert.That(opened.Attributes["application.address"], Is.EqualTo("http://api.test/"));
            Assert.ThrowsAsync<ObjectDisposedException>(() => hook.Client.GetAsync("/"), "the phase released its client");
        }
    }

    [Test]
    public async Task ApplicationClient_ShouldOpenTheInProcessTransportAndReleaseWhatItStarted()
    {
        var hook = new ClientHook("Api");
        var transport = new FakeTransport("Api");
        var builder = new ProtoHostBuilder();
        builder.ConfigureServices(services => services
            .AddSingleton<IProtoRunHook>(hook)
            .AddSingleton<IProtoClientInitializer>(transport));
        await using var host = builder.Build();

        await host.StartAsync();

        var opened = host.Trace.Snapshot().Entries!.Single(entry => entry.Kind == "run.application.client");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hook.Client!.BaseAddress, Is.EqualTo(new Uri("http://in-process/")));
            Assert.That(opened.Attributes["application.mode"], Is.EqualTo("in-process"));
            Assert.That(transport.Released, Is.True, "what the transport started only for the phase is released with it");
        }
    }

    [Test]
    public async Task ApplicationClient_ShouldNameTheAddressKeyWhenNothingServesTheApplication()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureServices(services => services.AddSingleton<IProtoRunHook>(new ClientHook("Api")));
        await using var host = builder.Build();

        var failure = Assert.ThrowsAsync<InvalidOperationException>(async () => await host.StartAsync());

        Assert.That(failure!.Message, Does.Contain("'ProtoTest:Applications:Api:BaseUrl'"));
    }

    [Test]
    public async Task ApplicationClient_ShouldNotBeReachableFromARunSetupStep()
    {
        Exception? failure = null;
        var builder = new ProtoHostBuilder();
        builder.AddRunSetup("read the api", async context =>
        {
            try
            {
                await context.ApplicationClientAsync("Api");
            }
            catch (InvalidOperationException exception)
            {
                failure = exception;
            }
        });
        await using var host = builder.Build();

        await host.StartAsync();

        Assert.That(failure?.Message, Does.Contain("IProtoRunHook.AfterInfrastructureAsync"));
    }

    [Test]
    public async Task AfterInfrastructure_ShouldReachACollectorThatIsARunHookOnce()
    {
        var collector = new LoadingCollector();
        var builder = new ProtoHostBuilder();
        builder.ConfigureServices(services => services
            .AddSingleton<IProtoCollector>(collector)
            .AddSingleton<IProtoRunHook>(collector));
        await using var host = builder.Build();

        await host.StartAsync();

        Assert.That(collector.Loads, Is.EqualTo(1));
    }

    private static ProtoHostBuilder Builder(List<string> events)
    {
        var builder = new ProtoHostBuilder();
        builder.AddInfrastructure(
            "thing",
            chain => chain.Use(new ProtoTargetProvider("fake", new RecordingInfrastructure(events))),
            Key);
        return builder;
    }

    private sealed class RecordingHook(List<string> events) : IProtoRunHook
    {
        public bool FailOnce { get; set; }

        public ProtoRunSetupContext? Context { get; private set; }

        public Task BeforeRunAsync(CancellationToken cancellationToken = default)
        {
            events.Add("before");
            return Task.CompletedTask;
        }

        public Task AfterInfrastructureAsync(ProtoRunSetupContext context)
        {
            events.Add("after-infrastructure");
            Context = context;
            if (FailOnce)
            {
                FailOnce = false;
                throw new InvalidOperationException("The hook failed.");
            }

            return Task.CompletedTask;
        }

        public Task AfterRunAsync(CancellationToken cancellationToken = default)
        {
            events.Add("after-run");
            return Task.CompletedTask;
        }
    }

    private sealed class ClientHook(string application) : IProtoRunHook
    {
        public HttpClient? Client { get; private set; }

        public async Task AfterInfrastructureAsync(ProtoRunSetupContext context)
            => Client = await context.ApplicationClientAsync(application);
    }

    private sealed class FakeTransport(string application) : IProtoClientInitializer<HttpClient>, IProtoRunApplicationTransport, IAsyncDisposable
    {
        public bool Released { get; private set; }

        public string Name => application;

        public string ApplicationName => application;

        public Task<bool> TryInitializeAsync(ProtoExecutionContext context) => Task.FromResult(false);

        public ValueTask<ProtoRunApplicationClient> OpenRunClientAsync(ProtoRunSetupContext context)
            => ValueTask.FromResult(new ProtoRunApplicationClient(new HttpClient { BaseAddress = new Uri("http://in-process/") }, this));

        public ValueTask DisposeAsync()
        {
            Released = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class LoadingCollector : IProtoCollector, IProtoRunHook
    {
        public int Loads { get; private set; }

        public bool CanCollect(ProtoObservation observation) => false;

        public void Collect(ProtoObservation observation)
        {
        }

        public IEnumerable<ProtoReportItem> GetReportItems() => [];

        public Task AfterInfrastructureAsync(ProtoRunSetupContext context)
        {
            Loads++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingInfrastructure(List<string> events) : IProtoConnectionInfrastructure
    {
        public string Id => "thing:recording";

        public string Kind => "thing";

        public string Description => "Recording thing";

        public ProtoResourceScope Scope => ProtoResourceScope.Run;

        public string ConnectionString => "fake://connection";

        public ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            events.Add("infrastructure");
            return ValueTask.CompletedTask;
        }

        public ValueTask ReleaseAsync(ProtoResourceReleaseContext context)
        {
            events.Add("released");
            return ValueTask.CompletedTask;
        }
    }
}
