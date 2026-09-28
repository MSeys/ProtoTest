namespace ProtoTest.Hosting.Tests;

using Microsoft.Extensions.Configuration;
using ProtoTest.Core;
using ProtoTest.Hosting.TestWorker;

/// <summary>
/// The worker provider chain through the real host builder: a worker nested under an application
/// follows its winner - hosted when the application runs in-process, left to the environment when a
/// configured provider serves it - and a top-level worker keeps hosting as its default.
/// </summary>
public sealed class WorkerProviderChainTests
{
    private static readonly string WorkerAssembly = typeof(Program).Assembly.GetName().Name!;

    [Test]
    public async Task NestedWorker_WhenTheApplicationRunsInProcess_ShouldBeHostedWithTheClock()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddApplication("Api", app =>
        {
            app.UseConfigured();
            app.Providers.Use(InProcessApplication(app.ApplicationName));
            app.AddWorkerHost<Program>("Billing");
        });
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("nested in-process worker", "00001", TestMethods.Placeholder);

        var probe = Proto.Context.HostService<Program, WorkerProbe>("Billing");
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var resolutions = host.Trace.Snapshot().Entries!
            .Where(entry => entry.Kind == ProtoTargetTrace.Resolved)
            .ToDictionary(entry => entry.Attributes["environment.target"]!, entry => entry);
        Assert.Multiple(() =>
        {
            Assert.That(probe.Started, Is.True, "the run hosts the worker's entry point");
            Assert.That(resolutions["Api"].Attributes["environment.provider"], Is.EqualTo("in-process"));
            Assert.That(
                resolutions["Api:worker:Billing"].Attributes["environment.provider"],
                Is.EqualTo("host"),
                "the worker follows the application into the process");
            Assert.That(host.HasCapability(ProtoCapabilityKinds.Worker, WorkerAssembly), Is.True);
            Assert.That(host.HasCapability(ProtoCapabilityKinds.Clock), Is.True, "the hosted worker declares the test clock");
        });
    }

    [Test]
    public async Task NestedWorker_WhenTheApplicationIsExternal_ShouldBeLeftToTheEnvironment()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:Api:BaseUrl"] = "http://127.0.0.1:9"
            }));
        builder.AddApplication("Api", app =>
        {
            app.UseConfigured();
            app.Providers.Use(ExternalApplication());
            app.AddWorkerHost<Program>("Billing");
        });
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("external worker", "00001", TestMethods.Placeholder);

        var missing = Assert.Throws<InvalidOperationException>(() => Proto.Context.Host<Program>("Billing"));
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var entries = host.Trace.Snapshot().Entries!;
        var resolutions = entries
            .Where(entry => entry.Kind == ProtoTargetTrace.Resolved)
            .ToDictionary(entry => entry.Attributes["environment.target"]!, entry => entry);
        var workerEntity = host.Trace.Snapshot().Entities!
            .Single(entity => entity.Id == "Billing");
        Assert.Multiple(() =>
        {
            Assert.That(missing!.Message, Does.Contain("No worker host named 'Billing'"), "the run starts no second consumer");
            Assert.That(resolutions["Api"].Attributes["environment.provider"], Is.EqualTo("configured"));
            Assert.That(resolutions["Api:worker:Billing"].Attributes["environment.provider"], Is.EqualTo("environment"));
            Assert.That(
                entries.Any(entry => entry.Kind == ProtoTargetTrace.ProviderSkipped
                    && entry.Attributes["environment.target"] == "Api:worker:Billing"
                    && entry.Attributes["environment.provider"] == "host"),
                Is.True,
                "the hosted provider is recorded skipped with the reason");
            Assert.That(workerEntity.State["infrastructure.state"], Is.EqualTo("skipped"));
            Assert.That(host.HasCapability(ProtoCapabilityKinds.Worker, WorkerAssembly), Is.True, "the environment runs that worker");
            Assert.That(host.HasCapability(ProtoCapabilityKinds.Clock), Is.False);
        });
    }

    [Test]
    public async Task NestedWorker_WhenTheApplicationDeclaresNoChain_ShouldBeHosted()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddApplication("Api", app => app.AddWorkerHost<Program>("Billing"));
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("chainless nested worker", "00001", TestMethods.Placeholder);

        var probe = Proto.Context.HostService<Program, WorkerProbe>("Billing");
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.That(probe.Started, Is.True, "an application without a chain hosts its worker, as before");
    }

    [Test]
    public async Task TopLevelWorkerChain_ShouldHostByDefault()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddWorkerHost<Program>("Billing", worker => worker.UseHost());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("top-level chain", "00001", TestMethods.Placeholder);

        var probe = Proto.Context.HostService<Program, WorkerProbe>("Billing");
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(probe.Started, Is.True);
            Assert.That(host.HasCapability(ProtoCapabilityKinds.Worker, WorkerAssembly), Is.True);
            Assert.That(host.HasCapability(ProtoCapabilityKinds.Clock), Is.True);
        });
    }

    [Test]
    public void TopLevelWorker_WithOnlyTheEnvironmentProvider_ShouldFailTheBuildNamingTheTarget()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddWorkerHost<Program>("Billing", worker => worker.UseEnvironment());
        var exception = Assert.Throws<ProtoTargetResolutionException>(() => builder.Build());

        Assert.Multiple(() =>
        {
            Assert.That(exception!.TargetName, Is.EqualTo("worker:Billing"));
            Assert.That(exception.Message, Does.Contain("The worker is not nested under an application"));
        });
    }

    private static ProtoTargetProvider InProcessApplication(string name)
        => new("in-process")
        {
            Capabilities =
            [
                new ProtoCapabilityDescriptor("ASP.NET Core", ProtoCapabilityKinds.Server, "Tests") { Instance = name },
                new ProtoCapabilityDescriptor("Test clock", ProtoCapabilityKinds.Clock, "Tests") { Instance = name }
            ]
        };

    private static ProtoTargetProvider ExternalApplication() => new("external");
}
