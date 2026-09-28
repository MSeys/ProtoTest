namespace ProtoTest.Aspire.Tests;

using Microsoft.Extensions.Configuration;
using ProtoTest.Aspire.TestAppHost;
using ProtoTest.Core;

/// <summary>
/// The Aspire providers through the real host builder: the AppHost serves when
/// <c>ProtoTest:Aspire:Enabled</c> selects it, a configured provider earlier in the chain wins over it,
/// and a mapped resource fills the target's declared key.
/// </summary>
public sealed class AspireProviderChainTests
{
    private static readonly string AppHostAssembly = typeof(TestAppHostAnchor).Assembly.GetName().Name!;

    [Test]
    public async Task UseAspireResource_WhenNotSelected_ShouldSkipTheAppHostAndLetTheFallbackWin()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddApplication("Api", app =>
        {
            app.UseConfigured();
            app.UseAspireResource<TestAppHostAnchor>("api");
            app.Providers.Use(new ProtoTargetProvider("fallback"));
        });
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StopAsync();

        var entries = host.Trace.Snapshot().Entries!;
        var resolved = entries.Single(entry => entry.Kind == ProtoTargetTrace.Resolved);
        var skipped = entries.Where(entry => entry.Kind == ProtoTargetTrace.ProviderSkipped).ToArray();
        var appHostSkip = skipped.Single(entry => entry.Attributes["environment.provider"] == "aspire:api");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolved.Attributes["environment.provider"], Is.EqualTo("fallback"));
            Assert.That(
                skipped.Select(entry => entry.Attributes["environment.provider"]),
                Is.EquivalentTo(new[] { "configured", "aspire:api" }));
            Assert.That(
                appHostSkip.Attributes["environment.reason"],
                Does.Contain("One of the selection keys must be set"),
                "the AppHost names the selection keys it needs");
            Assert.That(appHostSkip.Attributes["environment.reason"], Does.Contain(ProtoAspireOptions.SelectionKey));
            Assert.That(
                appHostSkip.Attributes["environment.reason"],
                Does.Contain(ProtoAspireOptions.ResourceSelectionKey("api")));
            Assert.That(
                host.HasCapability(ProtoCapabilityKinds.Aspire, AppHostAssembly),
                Is.False,
                "a losing AppHost declares nothing");
        }
    }

    [Test]
    public async Task UseAspireResource_WhenSelectedButAConfiguredProviderComesFirst_ShouldStillSkipTheAppHost()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:Api:BaseUrl"] = "http://127.0.0.1:9",
                [ProtoAspireOptions.SelectionKey] = "true"
            }));
        builder.AddApplication("Api", app =>
        {
            app.UseConfigured();
            app.UseAspireResource<TestAppHostAnchor>("api");
        });
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StopAsync();

        var entries = host.Trace.Snapshot().Entries!;
        var resolved = entries.Single(entry => entry.Kind == ProtoTargetTrace.Resolved);
        var skipped = entries.Single(entry => entry.Kind == ProtoTargetTrace.ProviderSkipped);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolved.Attributes["environment.provider"], Is.EqualTo("configured"));
            Assert.That(skipped.Attributes["environment.provider"], Is.EqualTo("aspire:api"));
            Assert.That(skipped.Attributes["environment.reason"], Does.Contain("earlier provider 'configured'"));
            Assert.That(
                host.Trace.Snapshot().Entities!.Single(entity => entity.Id.StartsWith("aspire:", StringComparison.Ordinal))
                    .State["infrastructure.state"],
                Is.EqualTo("skipped"),
                "a configured address wins before the AppHost could start");
        }
    }

    [Test]
    public void AspireResource_ShouldMapEveryDeclaredKeyToTheConnectionString()
    {
        var piece = new ProtoAspireAppHost<TestAppHostAnchor>(["db"], configure: null, publishEndpoints: false);
        piece.MapConnectionString("db", "ConnectionStrings:Store");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(piece.PublishKeys, Is.EqualTo(new[] { "ConnectionStrings:Store" }));
            Assert.That(piece.Publishes, Has.Count.EqualTo(1));
            Assert.That(piece.Publishes[0].Kind, Is.EqualTo(ProtoAspirePublishKind.ConnectionString));
        }
    }

    [Test]
    public async Task GlobalSelection_ShouldSelectEveryAppHostProvider()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [ProtoAspireOptions.SelectionKey] = "true"
            }));
        builder.AddAspireAppHost<TestAppHostAnchor>(
            options => options
                .Set("Aspire:Test:ConnectionString", "true")
                .MapConnectionString("db", "ConnectionStrings:Store"),
            "api",
            "db");
        builder.AddApplication("Api", app =>
        {
            app.UseConfigured();
            app.UseAspireResource<TestAppHostAnchor>("api");
        });
        builder.AddInfrastructure(
            "Store",
            chain => chain
                .UseConfigured()
                .UseAspireResource<TestAppHostAnchor>("db"),
            "ConnectionStrings:Store");
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

        await host.StartTestAsync("global selection", "00001", TestMethods.Placeholder);
        var address = Proto.Context.AspireResource("api");
        var store = Proto.Context.AspireResource("db");
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var resolved = host.Trace.Snapshot().Entries!
            .Where(entry => entry.Kind == ProtoTargetTrace.Resolved)
            .ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                resolved.Single(entry => entry.Attributes["environment.target"] == "Api")
                    .Attributes["environment.provider"],
                Is.EqualTo("aspire:api"),
                "the global key selects the application's AppHost provider");
            Assert.That(
                resolved.Single(entry => entry.Attributes["environment.target"] == "Store")
                    .Attributes["environment.provider"],
                Is.EqualTo("aspire:db"),
                "the global key selects the infrastructure target's AppHost provider");
            Assert.That(address, Does.StartWith("http://"));
            Assert.That(store, Is.EqualTo("Host=apphost"));
        }
    }

    [Test]
    public async Task ResourceSelection_ShouldServeOnlyThatResourcesTargets()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [ProtoAspireOptions.ResourceSelectionKey("db")] = "true"
            }));
        builder.AddAspireAppHost<TestAppHostAnchor>(
            options => options
                .Set("Aspire:Test:ConnectionString", "true")
                .MapConnectionString("db", "ConnectionStrings:Store"),
            "api",
            "db");
        builder.AddApplication("Api", app =>
        {
            app.UseConfigured();
            app.UseAspireResource<TestAppHostAnchor>("api");
            app.Providers.Use(new ProtoTargetProvider("fallback"));
        });
        builder.AddInfrastructure(
            "Store",
            chain => chain
                .UseConfigured()
                .UseAspireResource<TestAppHostAnchor>("db"),
            "ConnectionStrings:Store");
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
        var store = Proto.Context.AspireResource("db");
        var settings = Proto.Context.TryService<ProtoInfrastructureSettings>()!.Values;
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var resolved = host.Trace.Snapshot().Entries!
            .Where(entry => entry.Kind == ProtoTargetTrace.Resolved)
            .ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                resolved.Single(entry => entry.Attributes["environment.target"] == "Api")
                    .Attributes["environment.provider"],
                Is.EqualTo("fallback"),
                "the application target stays on its fallback while only the resource's key is set");
            Assert.That(
                resolved.Single(entry => entry.Attributes["environment.target"] == "Store")
                    .Attributes["environment.provider"],
                Is.EqualTo("aspire:db"),
                "the resource's own key selects its infrastructure target");
            Assert.That(store, Is.EqualTo("Host=apphost"));
            Assert.That(
                settings.ContainsKey("ProtoTest:Applications:api:BaseUrl"),
                Is.False,
                "the AppHost publishes only the selected resource's keys");
        }
    }

    [Test]
    public async Task UseAspireResource_WhenSelected_ShouldPublishTheAddressAndServeThroughTheChain()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [ProtoAspireOptions.SelectionKey] = "true"
            }));
        builder.AddApplication("Api", app =>
        {
            app.UseConfigured();
            app.UseAspireResource<TestAppHostAnchor>("api");
        });
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

        await host.StartTestAsync("aspire chain", "00001", TestMethods.Placeholder);
        var address = Proto.Context.AspireResource("api");
        using var http = new HttpClient();
        var body = await http.GetStringAsync(address);
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var resolved = host.Trace.Snapshot().Entries!
            .Single(entry => entry.Kind == ProtoTargetTrace.Resolved);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolved.Attributes["environment.provider"], Is.EqualTo("aspire:api"));
            Assert.That(address, Does.StartWith("http://"));
            Assert.That(body, Is.EqualTo("aspire-test-service"), "the mapped endpoint serves the suite");
            Assert.That(host.HasCapability(ProtoCapabilityKinds.Aspire, AppHostAssembly), Is.True);
        }
    }

    [Test]
    public async Task MapConnectionString_ShouldFillTheMappedKeyFromTheAppHost()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [ProtoAspireOptions.SelectionKey] = "true"
            }));
        builder.AddAspireAppHost<TestAppHostAnchor>(
            options => options
                .Set("Aspire:Test:ConnectionString", "true")
                .MapConnectionString("db", "ConnectionStrings:AppHost"),
            "api",
            "db");
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

        await host.StartTestAsync("aspire connection string", "00001", TestMethods.Placeholder);
        var mapped = Proto.Context.AspireResource("db");
        var settings = Proto.Context.TryService<ProtoInfrastructureSettings>()!.Values;
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(mapped, Is.EqualTo("Host=apphost"));
            Assert.That(settings["ConnectionStrings:AppHost"], Is.EqualTo("Host=apphost"), "the target's declared key is filled");
        }
    }
}
