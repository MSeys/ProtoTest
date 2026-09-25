namespace ProtoTest.AspNetCore.Tests;

using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProtoTest.Core;
using ProtoTest.Rest;
using SampleApi = ProtoTest.AspNetCore.SampleApi;

[TestFixture]
public sealed class PublishedApplicationTests
{
    [Test]
    public async Task AddAspNetCoreServer_WhenTheAddressIsConfigured_ShouldStepAsideForIt()
    {
        await using var published = await PublishedApi.StartAsync();
        using var trace = new TemporaryTrace("published");
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.OutputPath = trace.Path);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:Api:BaseUrl"] = published.Address
            }));
        builder.AddApplication("Api", app => app
            .AddAspNetCoreServer<SampleApi.Program>()
            .AddRest(rest => rest.AddClient("Api")));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync(
            "published application",
            TestMethods.Placeholder,
            [new ApplicationAttribute("Api")]);

        using var response = await context.Rest().GetAsync("/ping");
        var body = response.Content;
        var factory = context.TryServerFactory<SampleApi.Program>("Api");
        var error = Assert.Throws<InvalidOperationException>(
            () => context.ServerFactory<SampleApi.Program>("Api"));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var snapshot = host.Trace.Snapshot();
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(
                body,
                Is.EqualTo(PublishedApi.Marker),
                "the configured address serves the request, not the in-process TestServer");
            Assert.That(factory, Is.Null);
            Assert.That(error!.Message, Does.Contain("runs at"));
            Assert.That(error.Message, Does.Contain("no in-process server"));
            Assert.That(host.HasCapability(ProtoCapabilityKinds.Server, "ASP.NET Core"), Is.False);
            Assert.That(
                snapshot.Entities!.Any(entity => entity.Kind == ProtoTraceEntityKinds.Server),
                Is.False,
                "no in-process server entity is recorded");
            Assert.That(
                snapshot.Tests.Single().Entries!.Any(entry => entry.Kind == "aspnetcore.server.skipped"),
                Is.True,
                "the trace says why the server stepped aside");
        });
    }

    [Test]
    public async Task AddAspNetCoreServer_WithoutAConfiguredAddress_ShouldKeepTheInProcessServer()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddApplication("Api", app => app
            .AddAspNetCoreServer<SampleApi.Program>()
            .AddRest(rest => rest.AddClient("Api")));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync(
            "in-process application",
            TestMethods.Placeholder,
            [new ApplicationAttribute("Api")]);

        using var response = await context.Rest().GetAsync("/ping");
        var factory = context.TryServerFactory<SampleApi.Program>("Api");
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(factory, Is.Not.Null);
            Assert.That(host.HasCapability(ProtoCapabilityKinds.Server, "ASP.NET Core"), Is.True);
        });
    }

    [Test]
    public async Task AddAspNetCoreServer_WhenOnlyAStartedPiecePublishesTheAddress_ShouldKeepTheInProcessServer()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddInfrastructure(new PublishedAddressInfrastructure("Api", "http://127.0.0.1:1"));
        builder.AddApplication("Api", app => app
            .AddAspNetCoreServer<SampleApi.Program>()
            .AddRest(rest => rest.AddClient("Api")));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync(
            "infrastructure address",
            TestMethods.Placeholder,
            [new ApplicationAttribute("Api")]);

        var baseAddress = context.Client<HttpClient>("Api:Api").BaseAddress;
        var factory = context.TryServerFactory<SampleApi.Program>("Api");
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(
                baseAddress,
                Is.EqualTo(new Uri("http://127.0.0.1:1")),
                "HTTP clients follow the address a settings piece published, one precedence everywhere");
            Assert.That(
                factory,
                Is.Not.Null,
                "the in-process server stays: step-aside reads static configuration only (decided)");
            Assert.That(host.HasCapability(ProtoCapabilityKinds.Server, "ASP.NET Core"), Is.True);
        });
    }

    [Test]
    public async Task AddAspNetCoreServer_WhenTheConfiguredAddressIsMalformed_ShouldFailNamingTheKey()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:Api:BaseUrl"] = "not-a-url"
            }));
        builder.AddApplication("Api", app => app.AddAspNetCoreServer<SampleApi.Program>());
        await using var host = builder.Build();
        await host.StartAsync();

        // No REST client is registered, so the step-aside itself initializes the application's HTTP
        // client and is the layer that rejects the malformed address.
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await host.StartTestAsync(
                "malformed address",
                TestMethods.Placeholder,
                [new ApplicationAttribute("Api")]));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Message,
                Does.Contain("ProtoTest:Applications:Api:BaseUrl"),
                "the failure names the configuration key to fix");
            Assert.That(exception.Message, Does.Contain("cannot step aside"));
        });

        await host.StopAsync();
    }

    [Test]
    public async Task AddAspNetCoreServer_WhenThePublishedAddressIsDead_ShouldFailTheRequestInsteadOfFallingBackInProcess()
    {
        var port = TestNetworking.FreePort();
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:Api:BaseUrl"] = $"http://127.0.0.1:{port}"
            }));
        builder.AddApplication("Api", app => app
            .AddAspNetCoreServer<SampleApi.Program>()
            .AddRest(rest => rest.AddClient("Api")));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync(
            "dead published address",
            TestMethods.Placeholder,
            [new ApplicationAttribute("Api")]);

        var exception = Assert.ThrowsAsync<HttpRequestException>(async () => await context.Rest().GetAsync("/ping"));
        var factory = context.TryServerFactory<SampleApi.Program>("Api");

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Message,
                Does.Contain($"127.0.0.1:{port}"),
                "the request names the dead published address it tried");
            Assert.That(factory, Is.Null, "no in-process server was started as a silent fallback");
            Assert.That(
                host.HasCapability(ProtoCapabilityKinds.Server, "ASP.NET Core"),
                Is.False,
                "a configured address means the in-process server stepped aside");
        });
    }

    [Test]
    public async Task AddHttpReadiness_ForAnInProcessApplication_ShouldSkipAndSayInProcess()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddApplication("Api", app => app.AddAspNetCoreServer<SampleApi.Program>());
        builder.AddHttpReadiness("Api");
        await using var host = builder.Build();

        await host.StartAsync();
        await host.StopAsync();

        var entity = host.Trace.Snapshot().Entities!.Single(candidate => candidate.Kind == "readiness");
        Assert.That(
            entity.State["readiness.skipped"],
            Does.Contain("runs in-process"),
            "the server capability proves the in-process mode the probe reports");
    }

    private sealed class PublishedApi : IAsyncDisposable
    {
        public const string Marker = "the published instance answered";

        private readonly WebApplication _app;

        private PublishedApi(WebApplication app, string address)
        {
            _app = app;
            Address = address;
        }

        public string Address { get; }

        public static async Task<PublishedApi> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            var app = builder.Build();
            app.MapGet("/ping", () => Marker);
            await app.StartAsync();
            var address = app.Services
                .GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.First();
            return new PublishedApi(app, address.TrimEnd('/'));
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
