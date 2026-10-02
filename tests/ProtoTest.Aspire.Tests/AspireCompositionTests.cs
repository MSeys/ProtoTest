namespace ProtoTest.Aspire.Tests;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ProtoTest.Aspire.TestAppHost;
using ProtoTest.Core;

/// <summary>
/// The composition rules that need no orchestration runtime: registration shape, option validation
/// and the skip-condition errors.
/// </summary>
public sealed class AspireCompositionTests
{
    private static readonly string AppHostAssembly = typeof(TestAppHostAnchor).Assembly.GetName().Name!;

    private sealed class OtherAppHostAnchor;

    [Test]
    public void AddAspireAppHost_WhenTheSameEntryPointMapsDifferentResources_ShouldThrow()
    {
        var builder = new ProtoHostBuilder();
        builder.AddAspireAppHost<TestAppHostAnchor>("api");

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.AddAspireAppHost<OtherAppHostAnchor>("api"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("'api'"), "the failure names the resource");
            Assert.That(exception.Message, Does.Contain(typeof(TestAppHostAnchor).FullName!));
            Assert.That(exception.Message, Does.Contain(typeof(OtherAppHostAnchor).FullName!));
        }
    }

    [Test]
    public async Task AddAspireAppHost_WhenNotSelected_ShouldNotStartTheAppHost()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddAspireAppHost<TestAppHostAnchor>("api");
        await using var host = builder.Build();
        await host.StartAsync();

        await host.StartTestAsync("apphost not selected", "00001", TestMethods.Placeholder);
        var exception = Assert.Throws<InvalidOperationException>(() => Proto.Context.AspireResource("api"));
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var snapshot = host.Trace.Snapshot();
        var resolved = snapshot.Entries!.Single(entry => entry.Kind == ProtoTargetTrace.Resolved);
        var appHostSkip = snapshot.Entries!
            .Where(entry => entry.Kind == ProtoTargetTrace.ProviderSkipped)
            .Single(entry => entry.Attributes["environment.provider"] == $"aspire:{AppHostAssembly}");
        var entity = snapshot.Entities!.Single(entity => entity.Kind == "aspire");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolved.Attributes["environment.provider"], Is.EqualTo("unselected"));
            Assert.That(
                appHostSkip.Attributes["environment.reason"],
                Does.Contain(ProtoAspireOptions.SelectionKey),
                "the skip names the global selection key");
            Assert.That(
                appHostSkip.Attributes["environment.reason"],
                Does.Contain(ProtoAspireOptions.ResourceSelectionKey("api")),
                "the skip names the resource's own selection key");
            Assert.That(
                host.HasCapability(ProtoCapabilityKinds.Aspire, AppHostAssembly),
                Is.False,
                "an AppHost that never starts declares no capability");
            Assert.That(entity.State["infrastructure.state"], Is.EqualTo("skipped"));
            Assert.That(
                exception!.Message,
                Does.Contain(ProtoAspireOptions.SelectionKey),
                "the resource lookup names the selection keys");
        }
    }

    [Test]
    public async Task AddAspireAppHost_WhenSelectedButEveryResourceIsConfigured_ShouldStepAside()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:api:BaseUrl"] = "http://127.0.0.1:9",
                [ProtoAspireOptions.SelectionKey] = "true"
            }));
        builder.AddAspireAppHost<TestAppHostAnchor>("api");
        await using var host = builder.Build();
        await host.StartAsync();

        await host.StartTestAsync("apphost configured", "00001", TestMethods.Placeholder);
        var address = ProtoApplication.BaseUrl(Proto.Context, "api");
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var snapshot = host.Trace.Snapshot();
        var resolved = snapshot.Entries!.Single(entry => entry.Kind == ProtoTargetTrace.Resolved);
        var entity = snapshot.Entities!.Single(entity => entity.Kind == "aspire");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolved.Attributes["environment.provider"], Is.EqualTo("configured"));
            Assert.That(entity.State["infrastructure.state"], Is.EqualTo("skipped"));
            Assert.That(entity.State["infrastructure.reason"], Does.Contain("configured"));
            Assert.That(
                host.HasCapability(ProtoCapabilityKinds.Aspire, AppHostAssembly),
                Is.False,
                "a configured environment steps the AppHost aside without a capability");
            Assert.That(address, Is.EqualTo("http://127.0.0.1:9"), "the configured address serves the resource");
        }
    }

    [Test]
    public void AspireOptions_ShouldRejectAnUnusableComposition()
    {
        var builder = new ProtoHostBuilder();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                Assert.Throws<ArgumentException>(
                    () => builder.AddAspireAppHost<TestAppHostAnchor>("  ")),
                Is.Not.Null,
                "a blank resource name fails");
            Assert.That(
                Assert.Throws<ArgumentException>(
                    () => new ProtoAspireAppHost<TestAppHostAnchor>([])),
                Is.Not.Null,
                "no resources fails");
            Assert.That(
                Assert.Throws<ArgumentException>(
                    () => new ProtoAspireAppHost<TestAppHostAnchor>(["api", "api"])),
                Is.Not.Null,
                "a repeated resource fails");
            Assert.That(
                Assert.Throws<ArgumentException>(
                    () => new ProtoAspireAppHost<TestAppHostAnchor>(
                        ["api"],
                        options => options.MapResource("worker", "Worker"))),
                Is.Not.Null,
                "mapping an unregistered resource fails");
            Assert.That(
                Assert.Throws<ArgumentException>(
                    () => new ProtoAspireAppHost<TestAppHostAnchor>(
                        ["api", "worker"],
                        options =>
                        {
                            options.MapResource("api", "App");
                            options.MapResource("worker", "App");
                        })),
                Is.Not.Null,
                "two resources sharing one application fails");
            Assert.That(
                Assert.Throws<ArgumentException>(
                    () => new ProtoAspireAppHost<TestAppHostAnchor>(
                        ["api"],
                        options => options.UseEndpoint("worker", "http"))),
                Is.Not.Null,
                "an endpoint for an unregistered resource fails");
        }
    }

    [Test]
    public void AspireOptions_WhenResourcesAreMapped_ShouldPublishUnderTheApplicationName()
    {
        var piece = new ProtoAspireAppHost<TestAppHostAnchor>(
            ["api"],
            options =>
            {
                options.MapResource("api", "Api");
                options.UseEndpoint("api", "https");
            });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(piece.ApplicationFor("api"), Is.EqualTo("Api"));
            Assert.That(piece.EndpointName("api"), Is.EqualTo("https"));
            Assert.That(piece.BaseUrlKey("api"), Is.EqualTo("ProtoTest:Applications:Api:BaseUrl"));
            Assert.That(piece.BaseUrlKeys, Is.EqualTo(new[] { "ProtoTest:Applications:Api:BaseUrl" }));
        }
    }

    [Test]
    public async Task AspireResource_WhenTheHostHasNoAppHost_ShouldNameTheRegistration()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("no apphost", "00001", TestMethods.Placeholder);

        var exception = Assert.Throws<InvalidOperationException>(() => Proto.Context.AspireResource("api"));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.That(exception!.Message, Does.Contain("AddAspireAppHost"));
    }

    [Test]
    public async Task AspireResource_WhenTheResourceIsUnknown_ShouldListTheKnownOnes()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddAspireAppHost<TestAppHostAnchor>("api");
        await using var host = builder.Build();
        try
        {
            await host.StartAsync();
        }
        catch (ProtoAspireUnavailableException unavailable)
        {
            Assert.Ignore($"The Aspire orchestration runtime is unavailable: {unavailable.Message}");
            return;
        }

        await host.StartTestAsync("unknown resource", "00001", TestMethods.Placeholder);

        var exception = Assert.Throws<InvalidOperationException>(() => Proto.Context.AspireResource("worker"));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("'worker'"));
            Assert.That(exception.Message, Does.Contain("'api'"));
        }
    }

    [Test]
    public void Merge_ShouldLayerConfigurationThenSettingsThenOptions()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Config:Only"] = "configuration",
                ["Layered:Value"] = "configuration",
                ["Config:Blank"] = null
            })
            .Build();
        var settings = new Dictionary<string, string>
        {
            ["Settings:Only"] = "settings",
            ["Layered:Value"] = "settings"
        };
        var options = new Dictionary<string, string?>
        {
            ["Options:Only"] = "options",
            ["Layered:Value"] = "options"
        };

        var merged = ProtoAspireAppHost<TestAppHostAnchor>.Merge(configuration, settings, options);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                merged["Config:Only"],
                Is.EqualTo("configuration"),
                "the suite's configuration reaches the AppHost");
            Assert.That(
                merged["Settings:Only"],
                Is.EqualTo("settings"),
                "the settings earlier infrastructure published reach the AppHost");
            Assert.That(
                merged["Options:Only"],
                Is.EqualTo("options"),
                "the AppHost's own option values reach it last");
            Assert.That(
                merged["Layered:Value"],
                Is.EqualTo("options"),
                "each layer wins over the one before it: options over settings over configuration");
            Assert.That(
                merged["Config:Blank"],
                Is.Null,
                "a key with no value stays valueless for the AppHost's own sources");
        }
    }

    [Test]
    public void IsRuntimeMissing_ShouldRecognizeTheOrchestrationValidationFailure()
    {
        var missing = new OptionsValidationException(
            "Aspire",
            typeof(object),
            ["Property CliPath: The path to the DCP executable used for Aspire orchestration is required."]);
        var wrapped = new AggregateException(
            "The AppHost failed.",
            new InvalidOperationException("Inner start.", missing));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                ProtoAspireAppHost<TestAppHostAnchor>.IsRuntimeMissing(missing),
                Is.True,
                "the DCP validation failure is a missing runtime");
            Assert.That(
                ProtoAspireAppHost<TestAppHostAnchor>.IsRuntimeMissing(wrapped),
                Is.True,
                "the failure is recognized through wrappers");
            Assert.That(
                ProtoAspireAppHost<TestAppHostAnchor>.IsRuntimeMissing(
                    new InvalidOperationException("The test AppHost was told to fail at start.")),
                Is.False,
                "an application failure is not a missing runtime");
        }
    }

    [Test]
    public void IsRuntimeMissing_ShouldRecognizeADcpThatCouldNotStart()
    {
        // What a constrained runner records: Aspire's wait for DCP's API server times out.
        var timeout = new TimeoutException(
            "The operation didn't complete within the allowed timeout of '00:00:20'.",
            new DcpWaitCancelled());
        var resourceTimeout = new TimeoutException("The 'api' resource did not become healthy in time.");
        // A runner whose Docker CLI does not answer fails DCP's dependency check before any resource runs.
        var dependencyCheck = new InvalidOperationException(
            "The Aspire AppHost failed to start.",
            new DcpFailure("Application orchestrator dependency check returned an error: The operation has timed out.",
                "   at Aspire.Hosting.Dcp.DcpDependencyCheck.GetDcpInfoAsync(Boolean force, CancellationToken cancellationToken)"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ProtoAspireAppHost<TestAppHostAnchor>.IsRuntimeMissing(timeout), Is.True,
                "DCP never came up, so the runtime is unavailable on this machine");
            Assert.That(ProtoAspireAppHost<TestAppHostAnchor>.IsRuntimeMissing(dependencyCheck), Is.True,
                "a container runtime that does not answer leaves DCP unable to start");
            Assert.That(ProtoAspireAppHost<TestAppHostAnchor>.IsRuntimeMissing(resourceTimeout), Is.False,
                "a resource that times out is the application's failure");
        }
    }

    private sealed class DcpFailure(string message, string stackTrace) : Exception(message)
    {
        public override string StackTrace => stackTrace;
    }

    private sealed class DcpWaitCancelled() : OperationCanceledException("The operation was canceled.")
    {
        public override string StackTrace =>
            "   at Aspire.Hosting.Dcp.KubernetesService.EnsureKubernetesAsync(CancellationToken cancellationToken)";
    }
}
