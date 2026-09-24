namespace ProtoTest.Rest.Tests;

using System.Net.Http;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Http;

[TestFixture]
public sealed class ApplicationRestTests
{
    [Test]
    public async Task Application_ShouldResolveTheBoundRestClient()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:ControlPlane:BaseUrl"] = "https://app.test",
                ["ProtoTest:Applications:ControlPlane:Endpoints:Orders"] = "/api/orders",
                ["ProtoTest:Applications:ControlPlane:Endpoints:Billing"] = "/api/billing"
            }));
        builder.AddApplication("ControlPlane", app => app.AddRest(rest =>
        {
            rest.AddClient("Orders", endpoint: "Orders");
            rest.AddClient("Billing", endpoint: "Billing");
        }));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync(
            "rest application", TestMethods.Placeholder, [new ApplicationAttribute("ControlPlane", "Rest:Billing")]);

        Assert.That(context.Client<HttpClient>("ControlPlane:Billing").BaseAddress,
            Is.EqualTo(new Uri("https://app.test/api/billing")));

        context.Rest();
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var client = host.Trace.Snapshot().Tests.Single().Entities!
            .Single(entity => entity.Kind == ProtoTraceEntityKinds.Client
                && entity.Id.EndsWith("ControlPlane:Billing", StringComparison.Ordinal));
        Assert.That(client.Versions, Is.Not.Empty);
    }

    [Test]
    public async Task Rest_ShouldPreferAnExplicitCallSiteClient()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:ControlPlane:BaseUrl"] = "https://app.test",
                ["ProtoTest:Applications:ControlPlane:Endpoints:Orders"] = "/api/orders"
            }));
        builder.AddApplication("ControlPlane", app => app.AddRest(rest => rest.AddClient("Orders", endpoint: "Orders")));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("rest application", TestMethods.Placeholder, [new ApplicationAttribute("ControlPlane")]);

        context.Rest("Orders");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var client = host.Trace.Snapshot().Tests.Single().Entities!
            .Single(entity => entity.Kind == ProtoTraceEntityKinds.Client
                && entity.Id.EndsWith("ControlPlane:Orders", StringComparison.Ordinal));
        Assert.That(client.Versions, Is.Not.Empty);
    }

    [Test]
    public async Task Rest_ShouldFallBackToTheApplicationTransportRootedAtTheClientsEndpoint()
    {
        var handler = new TestHttpMessageHandler
        {
            ResponseFactory = () => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        };
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:ControlPlane:Endpoints:Orders"] = "/api/orders"
            }));
        builder.AddApplication("ControlPlane", app =>
        {
            app.AddRest(rest => rest.AddClient("Orders", endpoint: "Orders"));
            // Stands in for AddAspNetCoreServer: the transport the client has no URL to bypass.
            app.Services.AddSingleton(new ProtoApplicationTransport("ControlPlane", "ControlPlane"));
            app.Services.AddSingleton<IProtoClientInitializer>(
                new StubTransportInitializer("ControlPlane", "http://transport.test/", handler));
        });
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync(
            "rest transport fallback", TestMethods.Placeholder, [new ApplicationAttribute("ControlPlane")]);

        using var response = await context.Rest().GetAsync("orders/42");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.That(handler.LastRequest!.RequestUri, Is.EqualTo(new Uri("http://transport.test/api/orders/42")));
        Assert.That(context.Client<HttpClient>("ControlPlane:Orders").BaseAddress, Is.Null);
        Assert.That(response.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.OK));
    }

    [Test]
    public async Task Rest_ShouldFallBackToTheTransportRootedAtACustomRegisteredEndpoint()
    {
        var handler = new TestHttpMessageHandler
        {
            ResponseFactory = () => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        };
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                // The name-based default endpoint would be wrong; the registered endpoint must win.
                ["ProtoTest:Applications:ControlPlane:Endpoints:Orders"] = "/wrong",
                ["ProtoTest:Applications:ControlPlane:Endpoints:OrdersV2"] = "/api/v2/orders"
            }));
        builder.AddApplication("ControlPlane", app =>
        {
            app.AddRest(rest => rest.AddClient("Orders", endpoint: "OrdersV2"));
            app.Services.AddSingleton(new ProtoApplicationTransport("ControlPlane", "ControlPlane"));
            app.Services.AddSingleton<IProtoClientInitializer>(
                new StubTransportInitializer("ControlPlane", "http://transport.test/", handler));
        });
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync(
            "rest custom endpoint fallback", TestMethods.Placeholder, [new ApplicationAttribute("ControlPlane")]);

        using var response = await context.Rest().GetAsync("orders/42");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.That(handler.LastRequest!.RequestUri, Is.EqualTo(new Uri("http://transport.test/api/v2/orders/42")));
    }

    [Test]
    public async Task Rest_NamedClient_UnderAnApplication_ShouldUseItsOwnBaseAddress()
    {
        var handler = new TestHttpMessageHandler
        {
            ResponseFactory = () => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        };
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:ControlPlane:BaseUrl"] = "https://app.test"
            }));
        builder.AddApplication("ControlPlane", app => app.AddRest(rest =>
            rest.AddClient("OrdersV2", "https://source.example/v2/", http =>
                http.ConfigurePrimaryHttpMessageHandler(() => handler))));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync(
            "rest application named client", TestMethods.Placeholder, [new ApplicationAttribute("ControlPlane")]);

        using var response = await context.Rest("OrdersV2").GetAsync("orders/42");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.That(handler.LastRequest!.RequestUri, Is.EqualTo(new Uri("https://source.example/v2/orders/42")));
    }

    [Test]
    public async Task Rest_ExplicitClientName_UnderAnApplication_ShouldUseTheHostRegisteredClient()
    {
        var hostHandler = new TestHttpMessageHandler
        {
            ResponseFactory = () => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        };
        var transportHandler = new TestHttpMessageHandler
        {
            ResponseFactory = () => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        };
        var builder = new ProtoHostBuilder();
        builder.AddRest(rest => rest.AddClient("HostOrders", "https://host.example/api/", http =>
            http.ConfigurePrimaryHttpMessageHandler(() => hostHandler)));
        builder.AddApplication("ControlPlane", app =>
        {
            app.AddRest(rest => rest.AddClient("Orders"));
            app.Services.AddSingleton(new ProtoApplicationTransport("ControlPlane", "ControlPlane"));
            app.Services.AddSingleton<IProtoClientInitializer>(
                new StubTransportInitializer("ControlPlane", "http://transport.test/", transportHandler));
        });
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync(
            "rest explicit host client", TestMethods.Placeholder, [new ApplicationAttribute("ControlPlane")]);

        using var response = await context.Rest("HostOrders").GetAsync("orders/42");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.Multiple(() =>
        {
            Assert.That(hostHandler.LastRequest!.RequestUri, Is.EqualTo(new Uri("https://host.example/api/orders/42")));
            Assert.That(transportHandler.LastRequest, Is.Null);
            Assert.That(
                host.Trace.Snapshot().Tests.Single().Entities!
                    .Count(entity => entity.Kind == ProtoTraceEntityKinds.Client
                        && entity.Id.EndsWith("HostOrders", StringComparison.Ordinal)),
                Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task Rest_PerTestBaseAddressResolver_UnderAnApplication_ShouldKeepItsClientAndHandler()
    {
        var resolverHandler = new TestHttpMessageHandler
        {
            ResponseFactory = () => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        };
        var transportHandler = new TestHttpMessageHandler
        {
            ResponseFactory = () => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        };
        var builder = new ProtoHostBuilder();
        builder.AddApplication("ControlPlane", app =>
        {
            app.AddRest(rest => rest.AddClient(
                "Environment",
                _ => new Uri("https://resolved.example/api/"),
                http => http.ConfigurePrimaryHttpMessageHandler(() => resolverHandler)));
            app.Services.AddSingleton(new ProtoApplicationTransport("ControlPlane", "ControlPlane"));
            app.Services.AddSingleton<IProtoClientInitializer>(
                new StubTransportInitializer("ControlPlane", "http://transport.test/", transportHandler));
        });
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync(
            "rest resolver under application", TestMethods.Placeholder, [new ApplicationAttribute("ControlPlane")]);

        using var response = await context.Rest("Environment").GetAsync("orders/42");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.Multiple(() =>
        {
            Assert.That(resolverHandler.LastRequest!.RequestUri, Is.EqualTo(new Uri("https://resolved.example/api/orders/42")));
            Assert.That(transportHandler.LastRequest, Is.Null);
        });
    }

    [Test]
    public async Task Rest_ShouldKeepTheFirstBaseAddressResolverForARepeatedClientName()
    {
        var handler = new TestHttpMessageHandler
        {
            ResponseFactory = () => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        };
        var builder = new ProtoHostBuilder();
        builder.AddRest(rest =>
        {
            rest.AddClient("Environment", _ => new Uri("https://first.example/"));
            rest.AddClient("Environment", _ => new Uri("https://second.example/"),
                http => http.ConfigurePrimaryHttpMessageHandler(() => handler));
        });
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("rest resolver first wins", TestMethods.Placeholder);

        using var response = await context.Rest("Environment").GetAsync("orders/42");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.That(handler.LastRequest!.RequestUri, Is.EqualTo(new Uri("https://first.example/orders/42")));
    }

    [Test]
    public async Task Rest_ShouldKeepTheFirstRegisteredEndpointForARepeatedClientName()
    {
        var handler = new TestHttpMessageHandler
        {
            ResponseFactory = () => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        };
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:ControlPlane:Endpoints:OrdersV1"] = "/api/v1/orders",
                ["ProtoTest:Applications:ControlPlane:Endpoints:OrdersV2"] = "/api/v2/orders"
            }));
        builder.AddApplication("ControlPlane", app =>
        {
            app.AddRest(rest => rest.AddClient("Orders", endpoint: "OrdersV1"));
            app.AddRest(rest => rest.AddClient("Orders", endpoint: "OrdersV2"));
            app.Services.AddSingleton(new ProtoApplicationTransport("ControlPlane", "ControlPlane"));
            app.Services.AddSingleton<IProtoClientInitializer>(
                new StubTransportInitializer("ControlPlane", "http://transport.test/", handler));
        });
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync(
            "rest repeated endpoint", TestMethods.Placeholder, [new ApplicationAttribute("ControlPlane")]);

        using var response = await context.Rest().GetAsync("orders/42");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        // The first initializer owns the client, so the first endpoint must root the transport too.
        Assert.That(handler.LastRequest!.RequestUri, Is.EqualTo(new Uri("http://transport.test/api/v1/orders/42")));
    }


}
