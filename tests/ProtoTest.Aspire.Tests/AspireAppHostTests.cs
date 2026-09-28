namespace ProtoTest.Aspire.Tests;

using Microsoft.Extensions.Configuration;
using ProtoTest.Aspire.TestAppHost;
using ProtoTest.Core;

/// <summary>
/// The live AppHost journeys: the run starts the AppHost, a test resolves a resource address and
/// calls it over HTTP, and teardown releases it. Each needs the Aspire orchestration runtime; a
/// machine without it records the reason and the test skips.
/// </summary>
public sealed class AspireAppHostTests
{
    private static readonly string AppHostAssembly = typeof(TestAppHostAnchor).Assembly.GetName().Name!;

    [Test]
    public async Task AspireJourney_ShouldStartTheAppHostResolveItsAddressServeHttpAndRelease()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        SelectTheAppHost(builder);
        builder.AddAspireAppHost<TestAppHostAnchor>("api");
        builder.AddAspireAppHost<TestAppHostAnchor>("api");
        builder.AddHttpReadiness("api", "/");
        await using var host = builder.Build();

        try
        {
            await host.StartAsync();
        }
        catch (ProtoAspireUnavailableException exception)
        {
            Assert.Ignore($"The Aspire orchestration runtime is unavailable: {exception.Message}");
            return;
        }

        await host.StartTestAsync("aspire journey", "00001", TestMethods.Placeholder);
        var address = Proto.Context.AspireResource("api");
        using var http = new HttpClient();
        string body;
        using (var response = await http.GetAsync(address))
        {
            response.EnsureSuccessStatusCode();
            body = await response.Content.ReadAsStringAsync();
        }

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var entities = host.Trace.Snapshot().Entities!
            .Where(entity => entity.Kind == "aspire")
            .ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(address.StartsWith("http://", StringComparison.Ordinal), Is.True, $"the resource address is an HTTP URL, not '{address}'");
            Assert.That(body, Is.EqualTo("aspire-test-service"), "the test calls the AppHost's resource over HTTP");
            Assert.That(host.HasCapability("aspire", AppHostAssembly), Is.True);
            Assert.That(entities, Has.Length.EqualTo(1), "the repeated registration is one lifecycle with one entity");
            Assert.That(entities[0].Id, Is.EqualTo($"aspire:{typeof(TestAppHostAnchor).FullName}"));
        }

        // Teardown releases the AppHost: the published address stops answering.
        await ProtoReadiness.WaitAsync(
            "the AppHost released its resource",
            async cancellationToken =>
            {
                try
                {
                    using var released = await http.GetAsync(address, cancellationToken);
                    return false;
                }
                catch (HttpRequestException)
                {
                    return true;
                }
            },
            TimeSpan.FromSeconds(15),
            TimeSpan.FromMilliseconds(200));
    }

    [Test]
    public async Task AspireStart_WhenTheEntryPointThrows_ShouldFailNamingTheAppHost()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        SelectTheAppHost(builder);
        builder.AddAspireAppHost<TestAppHostAnchor>(
            options => options.Set("Aspire:Test:FailStart", "true"),
            "api");
        await using var host = builder.Build();

        Exception? failure = null;
        try
        {
            await host.StartAsync();
        }
        catch (ProtoAspireUnavailableException exception)
        {
            Assert.Ignore($"The Aspire orchestration runtime is unavailable: {exception.Message}");
            return;
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        Assert.That(failure, Is.Not.Null, "the throwing entry point fails the run start");
        Assert.That(failure, Is.Not.InstanceOf<ProtoAspireUnavailableException>());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure!.Message, Does.Contain(typeof(TestAppHostAnchor).FullName!));
            Assert.That(Flatten(failure), Does.Contain("told to fail at start"));
        }
    }

    [Test]
    public async Task AspireStart_WhenTheEndpointIsMissing_ShouldNameTheResourceAndEndpoint()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        SelectTheAppHost(builder);
        builder.AddAspireAppHost<TestAppHostAnchor>(
            options => options.UseEndpoint("api", "metrics"),
            "api");
        await using var host = builder.Build();

        Exception? failure = null;
        try
        {
            await host.StartAsync();
        }
        catch (ProtoAspireUnavailableException exception)
        {
            Assert.Ignore($"The Aspire orchestration runtime is unavailable: {exception.Message}");
            return;
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        Assert.That(failure, Is.Not.Null, "an endpoint the resource does not expose fails the run start");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure!.Message, Does.Contain("'api'"));
            Assert.That(failure.Message, Does.Contain("'metrics'"));
        }
    }

    [Test]
    public async Task AspireResource_WhenTheAddressIsConfigured_ShouldResolveItWithoutStarting()
    {
        var piece = new ProtoAspireAppHost<TestAppHostAnchor>(["api"]);
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        SelectTheAppHost(builder);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:api:BaseUrl"] = "http://127.0.0.1:9",
            }));
        builder.AddInfrastructure(
            "AppHost",
            chain => chain
                .UseConfigured()
                .Use(new ProtoTargetProvider(
                    "apphost",
                    piece,
                    ProtoProviderConditions.Selected(
                        ProtoAspireOptions.SelectionKey,
                        ProtoAspireOptions.ResourceSelectionKey("api")))),
            piece.BaseUrlKeys.ToArray());
        await using var host = builder.Build();
        await host.StartAsync();

        await host.StartTestAsync("configured address", "00001", TestMethods.Placeholder);
        var address = ProtoApplication.BaseUrl(Proto.Context, "api");
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(piece.IsStarted, Is.False, "a configured key satisfies the piece, so the AppHost never starts");
            Assert.That(address, Is.EqualTo("http://127.0.0.1:9"), "the test resolves the configured topology address");
        }
    }

    [Test]
    public async Task AspireResources_WhenOnlySomeAddressesAreConfigured_ShouldPublishTheMissingOnly()
    {
        // One of the two declared keys is configured: the piece still starts, and only the missing
        // key is published, so the configured address is not masked.
        var piece = new ProtoAspireAppHost<TestAppHostAnchor>(
            ["api", "api2"],
            options => options.Set("Aspire:Test:TwoResources", "true"));
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        SelectTheAppHost(builder);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:api:BaseUrl"] = "http://127.0.0.1:9",
            }));
        builder.AddInfrastructure(
            "AppHost",
            chain => chain
                .UseConfigured()
                .Use(new ProtoTargetProvider("apphost", piece)),
            piece.BaseUrlKeys.ToArray());
        await using var host = builder.Build();

        try
        {
            await host.StartAsync();
        }
        catch (ProtoAspireUnavailableException exception)
        {
            Assert.Ignore($"The Aspire orchestration runtime is unavailable: {exception.Message}");
            return;
        }

        await host.StartTestAsync("partial configuration", "00001", TestMethods.Placeholder);
        var configured = ProtoApplication.BaseUrl(Proto.Context, "api");
        var published = ProtoApplication.BaseUrl(Proto.Context, "api2");
        var started = piece.IsStarted;
        var publishedKeys = piece.Settings.Keys.ToArray();
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(started, Is.True, "one missing key still starts the AppHost");
            Assert.That(
                publishedKeys,
                Is.EquivalentTo(new[] { "ProtoTest:Applications:api2:BaseUrl" }),
                "the AppHost publishes only the key configuration does not fill");
            Assert.That(configured, Is.EqualTo("http://127.0.0.1:9"), "the configured address wins for its resource");
            Assert.That(published, Does.StartWith("http://"), "the missing key resolves the AppHost's address");
        }
    }

    [Test]
    public async Task AspireResources_WhenOnlyOneIsSelected_ShouldPublishOnlyThatResource()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [ProtoAspireOptions.ResourceSelectionKey("api2")] = "true"
            }));
        builder.AddAspireAppHost<TestAppHostAnchor>(
            options => options.Set("Aspire:Test:TwoResources", "true"),
            "api",
            "api2");
        await using var host = builder.Build();

        try
        {
            await host.StartAsync();
        }
        catch (ProtoAspireUnavailableException exception)
        {
            Assert.Ignore($"The Aspire orchestration runtime is unavailable: {exception.Message}");
            return;
        }

        await host.StartTestAsync("resource selection", "00001", TestMethods.Placeholder);
        var published = Proto.Context.AspireResource("api2");
        var settings = Proto.Context.TryService<ProtoInfrastructureSettings>()!.Values;
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(published, Does.StartWith("http://"), "the selected resource resolves the AppHost's address");
            Assert.That(
                settings.ContainsKey("ProtoTest:Applications:api:BaseUrl"),
                Is.False,
                "the unselected resource stays on its other providers");
        }
    }

    [Test]
    public async Task AspireStart_ShouldForwardTheSuitesConfigurationAndTheSettingsEarlierInfrastructurePublished()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        SelectTheAppHost(builder);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Aspire:Test:Echo"] = "from-configuration"
            }));
        builder.AddInfrastructure(
            "SuiteSettings",
            chain => chain.Use(new ProtoTargetProvider(
                "suite-settings",
                new DeclaredSettingsInfrastructure("suite-settings", "Aspire:Test:SettingsEcho", "from-settings"))),
            "Aspire:Test:SettingsEcho");
        builder.AddAspireAppHost<TestAppHostAnchor>("api", "echo", "settings-echo");
        builder.MapConnectionString("echo", "ConnectionStrings:Echo");
        builder.MapConnectionString("settings-echo", "ConnectionStrings:SettingsEcho");
        await using var host = builder.Build();

        try
        {
            await host.StartAsync();
        }
        catch (ProtoAspireUnavailableException exception)
        {
            Assert.Ignore($"The Aspire orchestration runtime is unavailable: {exception.Message}");
            return;
        }

        await host.StartTestAsync("apphost settings bridge", "00001", TestMethods.Placeholder);
        var echoed = Proto.Context.AspireResource("echo");
        var settingsEchoed = Proto.Context.AspireResource("settings-echo");
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                echoed,
                Is.EqualTo("from-configuration"),
                "the suite's configuration reaches the AppHost's own graph");
            Assert.That(
                settingsEchoed,
                Is.EqualTo("from-settings"),
                "the settings earlier infrastructure published reach the AppHost's own graph");
        }
    }

    /// <summary>Sets the global AppHost selection key; the AppHost serves only when selected.</summary>
    private static void SelectTheAppHost(ProtoHostBuilder builder)
        => builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [ProtoAspireOptions.SelectionKey] = "true"
            }));

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
}
