namespace ProtoTest.AspNetCore.Tests;

using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Primitives;
using ProtoTest.Core;
using ProtoTest.Rest;

public class IntegrationTests
{
    [Test]
    public async Task AddAspNetCoreServer_Should_Initialize_InMemory_Server_And_HttpClient()
    {
        // 1. Arrange & Build ProtoHost
        var host = new ProtoHostBuilder()
            .AddAspNetCoreServer<SampleApi.Program>("Default")
            .Build();

        var context = await host.StartTestAsync("InMemory_Test", "00001", (MethodInfo)MethodInfo.GetCurrentMethod()!);

        try
        {
            // 2. Act: Call in-memory endpoint via Proto.Context
            var client = Proto.Context.Client<HttpClient>("Default");
            var response = await client.GetAsync("/ping");

            // 3. Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            // Validate direct Server Service resolution
            var service = Proto.Context.ServerService<SampleApi.Program, SampleApi.ITestMessageService>("Default");
            Assert.That(service.GetMessage(), Is.EqualTo("Hello from AspNetCore DI!"));
        }
        finally
        {
            await host.CompleteTestAsync();
            await host.DisposeAsync();
        }
    }

    [Test]
    public async Task InProcessInventory_ShouldRecordPageLikeEndpointsAndExcludeApiRoutes()
    {
        var host = new ProtoHostBuilder()
            .AddAspNetCoreServer<SampleApi.Program>("InventoryApi")
            .Build();
        await using var ownedHost = host;
        await host.StartTestAsync("PageInventory_Test", "00011", (MethodInfo)MethodInfo.GetCurrentMethod()!);

        var pages = Proto.Context.RecordedObservations
            .Where(observation => observation.Kind == "web.page.available")
            .Select(observation => observation.Identifier)
            .ToArray();
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.Multiple(() =>
        {
            Assert.That(pages, Does.Contain("/welcome"), "an endpoint producing text/html is page-like");
            Assert.That(pages, Does.Contain("/portal/home"), "an explicit GET endpoint producing text/html is page-like");
            Assert.That(pages, Does.Not.Contain("/ping"), "a JSON minimal API endpoint is not a page");
            Assert.That(pages, Does.Not.Contain("/any-method"), "an endpoint without explicit GET metadata is not a page");
            Assert.That(pages, Does.Not.Contain("/api/orders"), "API-shaped routes are excluded");
            Assert.That(pages, Does.Not.Contain("/orders/{id}"), "parameterized routes are not inventoried");
        });
    }

    [Test]
    public async Task InProcessInventory_ShouldHonorApplicationPageFiltersAndRecordOncePerRun()
    {
        var host = new ProtoHostBuilder()
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ProtoTest:Applications:InventoryApi:Web:Pages:Include:0"] = "/portal/*",
                    ["ProtoTest:Applications:InventoryApi:Web:Pages:Exclude:0"] = "/portal/legacy/*"
                }))
            .AddAspNetCoreServer<SampleApi.Program>("InventoryApi")
            .Build();
        await using var ownedHost = host;
        var method = (MethodInfo)MethodInfo.GetCurrentMethod()!;

        await host.StartTestAsync("PageInventoryFilter_Test", "00012", method);
        var first = Proto.Context.RecordedObservations
            .Where(observation => observation.Kind == "web.page.available")
            .Select(observation => observation.Identifier)
            .ToArray();
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        await host.StartTestAsync("PageInventoryReuse_Test", "00013", method);
        var second = Proto.Context.RecordedObservations
            .Where(observation => observation.Kind == "web.page.available")
            .Select(observation => observation.Identifier)
            .ToArray();
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.Multiple(() =>
        {
            Assert.That(first, Does.Contain("/portal/home"), "the include glob keeps the portal page");
            Assert.That(first, Does.Not.Contain("/portal/legacy/old"), "the exclude glob drops the legacy page");
            Assert.That(first, Does.Not.Contain("/welcome"), "the include glob drops the non-matching page");
            Assert.That(second, Is.Empty, "the run-level inventory is recorded once, by the first test");
        });
    }

    [Test]
    public async Task InProcessInventory_ShouldRequireHtmlEvidenceForControllerActions()
    {
        var host = new ProtoHostBuilder()
            .AddAspNetCoreServer<SampleApi.Program>("ControllerInventory")
            .Build();
        await using var ownedHost = host;
        await host.StartTestAsync("ControllerInventory_Test", "00014", (MethodInfo)MethodInfo.GetCurrentMethod()!);

        var pages = Proto.Context.RecordedObservations
            .Where(observation => observation.Kind == "web.page.available")
            .Select(observation => observation.Identifier)
            .ToArray();
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.Multiple(() =>
        {
            Assert.That(pages, Does.Not.Contain("/mvc/json"), "a JSON MVC action is not a page");
            Assert.That(pages, Does.Contain("/mvc/html"), "an MVC action producing text/html is a page");
            Assert.That(pages, Does.Contain("/mvc/view"), "a view-result action is a page");
            Assert.That(pages, Does.Not.Contain("/api/legacy/orders"), "a JSON API action is not a page");
            Assert.That(pages, Does.Contain("/api/legacy/report"),
                "an HTML-producing API controller action is a page even under an API-shaped route");
            Assert.That(pages, Does.Contain("/api/legacy/view"),
                "a view-returning API controller action is a page even under an API-shaped route");
        });
    }

    [Test]
    public async Task InProcessInventory_ShouldReadScalarIncludeAndExcludeValues()
    {
        var host = new ProtoHostBuilder()
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ProtoTest:Applications:ScalarApi:Web:Pages:Include"] = "/portal/*",
                    ["ProtoTest:Applications:ScalarApi:Web:Pages:Exclude"] = "/portal/legacy/*"
                }))
            .AddAspNetCoreServer<SampleApi.Program>("ScalarApi")
            .Build();
        await using var ownedHost = host;
        await host.StartTestAsync("ScalarFilter_Test", "00015", (MethodInfo)MethodInfo.GetCurrentMethod()!);

        var pages = Proto.Context.RecordedObservations
            .Where(observation => observation.Kind == "web.page.available")
            .Select(observation => observation.Identifier)
            .ToArray();
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.Multiple(() =>
        {
            Assert.That(pages, Does.Contain("/portal/home"), "a scalar Include keeps the matching page");
            Assert.That(pages, Does.Not.Contain("/portal/legacy/old"), "a scalar Exclude drops the matching page");
            Assert.That(pages, Does.Not.Contain("/welcome"), "the include still filters out everything else");
        });
    }

    [Test]
    public async Task InProcessInventory_ShouldRetryAfterAFailedDiscovery()
    {
        var flaky = new FlakyEndpointDataSource();
        var host = new ProtoHostBuilder()
            .AddAspNetCoreServer<SampleApi.Program>(
                "RetryApi",
                webHost => webHost.ConfigureTestServices(services => services.AddSingleton<EndpointDataSource>(flaky)))
            .Build();
        await using var ownedHost = host;
        var method = (MethodInfo)MethodInfo.GetCurrentMethod()!;

        await host.StartTestAsync("PageInventoryRetry_First", "00016", method);
        var first = Proto.Context.RecordedObservations
            .Where(observation => observation.Kind == "web.page.available")
            .Select(observation => observation.Identifier)
            .ToArray();
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        await host.StartTestAsync("PageInventoryRetry_Second", "00017", method);
        var second = Proto.Context.RecordedObservations
            .Where(observation => observation.Kind == "web.page.available")
            .Select(observation => observation.Identifier)
            .ToArray();
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Empty, "the failed discovery recorded nothing");
            Assert.That(second, Does.Contain("/welcome"), "the later test retried because the flag was reset");
            Assert.That(
                host.Trace.Snapshot().Tests.SelectMany(test => test.Entries)
                    .Any(entry => entry.Kind == "web.page.inventory.failed"),
                Is.True);
        });
    }

    [Test]
    public async Task InProcessInventory_ShouldRetryAfterAnEmptyDiscovery()
    {
        SampleApi.LatePageState.Enabled = false;
        var host = new ProtoHostBuilder()
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    // Only the late page is in scope: the first discovery is empty because it is not
                    // mapped yet.
                    ["ProtoTest:Applications:LateApi:Web:Pages:Include"] = "/late"
                }))
            .AddAspNetCoreServer<SampleApi.Program>(
                "LateApi",
                lifetime: AspNetCoreServerLifetime.PerTest)
            .Build();
        await using var ownedHost = host;
        var method = (MethodInfo)MethodInfo.GetCurrentMethod()!;

        await host.StartTestAsync("EmptyInventory_First", "00018", method);
        var first = Proto.Context.RecordedObservations
            .Where(observation => observation.Kind == "web.page.available")
            .Select(observation => observation.Identifier)
            .ToArray();
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        SampleApi.LatePageState.Enabled = true;
        try
        {
            await host.StartTestAsync("EmptyInventory_Second", "00019", method);
            var second = Proto.Context.RecordedObservations
                .Where(observation => observation.Kind == "web.page.available")
                .Select(observation => observation.Identifier)
                .ToArray();
            await host.CompleteTestAsync(ProtoTestResult.Passed);

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.Empty, "an empty discovery records nothing");
                Assert.That(second, Does.Contain("/late"),
                    "an empty discovery does not latch, so an endpoint added later is still inventoried");
            });
        }
        finally
        {
            SampleApi.LatePageState.Enabled = false;
        }
    }

    [Test]
    public async Task Rest_First_Should_Use_External_Client_When_BaseUrl_Is_Configured()
    {
        // 1. Arrange: Provide Configuration override for the client BaseUrl
        var inMemoryConfig = new Dictionary<string, string?>
        {
            { "ProtoTest:Applications:ExternalApi:BaseUrl", "https://api.example.com" }
        };

        var host = new ProtoHostBuilder()
            .ConfigureAppConfiguration(builder => builder.AddInMemoryCollection(inMemoryConfig))
            .AddRest(rest => rest.AddClient("ExternalApi"))
            .AddAspNetCoreServer<SampleApi.Program>("ExternalApi")
            .Build();

        var context = await host.StartTestAsync("ExternalUrl_Test", "00002", (MethodInfo)MethodInfo.GetCurrentMethod()!);

        try
        {
            // 2. Act
            var client = Proto.Context.Client<HttpClient>("ExternalApi");

            // 3. Assert: REST handled the client before the ASP.NET Core fallback was reached.
            Assert.That(client.BaseAddress, Is.EqualTo(new Uri("https://api.example.com")));
            Assert.That(
                Proto.Context.TryClient<WebApplicationFactory<SampleApi.Program>>("ExternalApi:Factory"),
                Is.Null);
        }
        finally
        {
            await host.CompleteTestAsync();
            await host.DisposeAsync();
        }
    }

    [Test]
    public async Task Rest_First_Should_Fall_Through_To_AspNetCore_When_BaseUrl_Is_Missing()
    {
        var host = new ProtoHostBuilder()
            .AddRest(rest => rest.AddClient("OrderApi"))
            .AddAspNetCoreServer<SampleApi.Program>("OrderApi")
            .Build();

        await host.StartTestAsync("LocalFallback_Test", "00004", (MethodInfo)MethodInfo.GetCurrentMethod()!);

        try
        {
            var response = await Proto.Context.Rest("OrderApi").GetAsync("/ping");

            response.Should.HaveHttpStatus(HttpStatusCode.OK);
            Assert.That(Proto.Context.ServerFactory<SampleApi.Program>("OrderApi"), Is.Not.Null);
        }
        finally
        {
            await host.CompleteTestAsync();
            await host.DisposeAsync();
        }
    }

    [Test]
    public async Task AspNetCore_First_Should_Take_Precedence_Over_Configured_Rest_Client()
    {
        var inMemoryConfig = new Dictionary<string, string?>
        {
            ["ProtoTest:Applications:OrderApi:BaseUrl"] = "https://api.example.com"
        };

        var host = new ProtoHostBuilder()
            .ConfigureAppConfiguration(builder => builder.AddInMemoryCollection(inMemoryConfig))
            .AddAspNetCoreServer<SampleApi.Program>("OrderApi")
            .AddRest(rest => rest.AddClient("OrderApi"))
            .Build();

        await host.StartTestAsync("RegistrationOrder_Test", "00005", (MethodInfo)MethodInfo.GetCurrentMethod()!);

        try
        {
            var response = await Proto.Context.Client<HttpClient>("OrderApi").GetAsync("/ping");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(Proto.Context.ServerFactory<SampleApi.Program>("OrderApi"), Is.Not.Null);
        }
        finally
        {
            await host.CompleteTestAsync();
            await host.DisposeAsync();
        }
    }

    [Test]
    public async Task AddAspNetCoreServer_Should_Invoke_FactoryConfigurationCallback()
    {
        // Arrange
        var callbackInvoked = false;
        var host = new ProtoHostBuilder()
            .AddAspNetCoreServer<SampleApi.Program>(
                "ConfiguredApi",
                webHost =>
                {
                    callbackInvoked = true;
                    webHost.UseSetting("ProtoTest:Configured", "true");
                })
            .Build();

        await host.StartTestAsync("FactoryConfiguration", "00003", (MethodInfo)MethodInfo.GetCurrentMethod()!);

        try
        {
            // Act
            // Assert
            Assert.That(callbackInvoked, Is.True);
            Assert.That(Proto.Context.Client<HttpClient>("ConfiguredApi"), Is.Not.Null);
        }
        finally
        {
            await host.CompleteTestAsync();
            await host.DisposeAsync();
        }
    }

    [Test]
    public async Task AddAspNetCoreServer_ShouldApplyWebHostServiceOverridesAndClientOptions()
    {
        var host = new ProtoHostBuilder()
            .AddAspNetCoreServer<SampleApi.Program>(
                "ConfiguredApi",
                webHost => webHost.ConfigureTestServices(services =>
                {
                    services.RemoveAll<SampleApi.ITestMessageService>();
                    services.AddSingleton<SampleApi.ITestMessageService, ReplacementMessageService>();
                }),
                client => client.BaseAddress = new Uri("https://configured.example.test"))
            .Build();

        await host.StartTestAsync(
            "ConfiguredServices",
            "00006",
            (MethodInfo)MethodInfo.GetCurrentMethod()!);

        try
        {
            var client = Proto.Context.Client<HttpClient>("ConfiguredApi");
            using var response = await client.GetAsync("/message");
            var body = await response.Content.ReadAsStringAsync();

            Assert.That(client.BaseAddress, Is.EqualTo(new Uri("https://configured.example.test")));
            Assert.That(body, Does.Contain("replacement"));
        }
        finally
        {
            await host.CompleteTestAsync();
            await host.DisposeAsync();
        }
    }

    [Test]
    public async Task PerRunLifetime_ShouldShareOneApplicationAcrossTests_AndDisposeItWithTheHost()
    {
        var host = new ProtoHostBuilder()
            .AddAspNetCoreServer<SampleApi.Program>(
                "SharedApi",
                webHost => webHost.UseSetting("ProtoTest:Configured", "true"))
            .Build();
        var method = (MethodInfo)MethodInfo.GetCurrentMethod()!;

        await host.StartTestAsync("First", "00007", method);
        var first = Proto.Context.ServerFactory<SampleApi.Program>("SharedApi");
        using (var response = await Proto.Context.Client<HttpClient>("SharedApi").GetAsync("/ping"))
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        await host.StartTestAsync("Second", "00008", method);
        var second = Proto.Context.ServerFactory<SampleApi.Program>("SharedApi");
        using (var response = await Proto.Context.Client<HttpClient>("SharedApi").GetAsync("/ping"))
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
        var initializations = host.Trace.Snapshot().Tests
            .SelectMany(test => test.Entries)
            .Where(entry => entry.Kind == "aspnetcore.server.initialize")
            .Select(entry => entry.Attributes["aspnetcore.server.reused"])
            .ToArray();
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.SameAs(first));
            Assert.That(initializations, Is.EqualTo(new[] { "false", "true" }));
            Assert.That(ResolveMessage(first), Is.EqualTo("Hello from AspNetCore DI!"));
        });

        await host.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => ResolveMessage(first));
    }

    [Test]
    public async Task PerTestLifetime_ShouldStartAndDisposeAnApplicationForEachTest()
    {
        var host = new ProtoHostBuilder()
            .AddAspNetCoreServer<SampleApi.Program>(
                "IsolatedApi",
                webHost => webHost.UseSetting("ProtoTest:Configured", "true"),
                lifetime: AspNetCoreServerLifetime.PerTest)
            .Build();
        await using var ownedHost = host;
        var method = (MethodInfo)MethodInfo.GetCurrentMethod()!;

        await host.StartTestAsync("First", "00009", method);
        var first = Proto.Context.ServerFactory<SampleApi.Program>("IsolatedApi");
        Assert.That(ResolveMessage(first), Is.EqualTo("Hello from AspNetCore DI!"));
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.Throws<ObjectDisposedException>(() => ResolveMessage(first));

        await host.StartTestAsync("Second", "00010", method);
        var second = Proto.Context.ServerFactory<SampleApi.Program>("IsolatedApi");
        Assert.Multiple(() =>
        {
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(ResolveMessage(second), Is.EqualTo("Hello from AspNetCore DI!"));
        });
        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    private static string ResolveMessage(WebApplicationFactory<SampleApi.Program> factory)
        => factory.Services.GetRequiredService<SampleApi.ITestMessageService>().GetMessage();

    private sealed class ReplacementMessageService : SampleApi.ITestMessageService
    {
        public string GetMessage() => "replacement";
    }

    /// <summary>
    /// An endpoint source whose first read (startup's authorization policy cache) succeeds and whose
    /// second read (the first page inventory) throws, so a later test's inventory can still succeed.
    /// </summary>
    private sealed class FlakyEndpointDataSource : EndpointDataSource
    {
        private int _attempts;

        public override IReadOnlyList<Endpoint> Endpoints
            => Interlocked.Increment(ref _attempts) == 2
                ? throw new InvalidOperationException("endpoint discovery exploded")
                : [];

        public override IChangeToken GetChangeToken() => new CancellationChangeToken(CancellationToken.None);
    }
}
