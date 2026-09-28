namespace ProtoTest.AspNetCore.Tests;

using System.Net;
using Microsoft.Extensions.Configuration;
using ProtoTest.Core;
using ProtoTest.Rest;
using SampleApi = ProtoTest.AspNetCore.SampleApi;

/// <summary>
/// The application provider chain through the real host builder: the first provider whose condition
/// holds serves the application, only the winner's capabilities are declared, and a non-chain
/// registration keeps today's behavior.
/// </summary>
[TestFixture]
public sealed class ApplicationProviderChainTests
{
    [Test]
    public async Task InProcessProvider_ShouldDeclareServerAndClockAndServeInProcess()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddApplication("Api", app => app
            .UseConfigured()
            .UseInProcess<SampleApi.Program>()
            .AddRest(rest => rest.AddClient("Api")));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync(
            "in-process chain",
            TestMethods.Placeholder,
            [new ApplicationAttribute("Api")]);

        using var response = await context.Rest().GetAsync("/ping");
        var body = response.Content;
        var factory = context.TryServerFactory<SampleApi.Program>("Api");
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var resolved = host.Trace.Snapshot().Entries!
            .Single(entry => entry.Kind == ProtoTargetTrace.Resolved);
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Is.EqualTo("{\"message\":\"pong\"}"), "the in-process TestServer serves the application");
            Assert.That(factory, Is.Not.Null);
            Assert.That(host.HasCapability(ProtoCapabilityKinds.Server, "ASP.NET Core", "Api"), Is.True);
            Assert.That(host.HasCapability(ProtoCapabilityKinds.Clock, "Test clock", "Api"), Is.True);
            Assert.That(resolved.Attributes["environment.target"], Is.EqualTo("Api"));
            Assert.That(resolved.Attributes["environment.provider"], Is.EqualTo("in-process"));
        });
    }

    [Test]
    public async Task ConfiguredProvider_ShouldWinAndRegisterNoTestServerOrClock()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:Api:BaseUrl"] = "http://127.0.0.1:9"
            }));
        builder.AddApplication("Api", app => app
            .UseConfigured()
            .UseInProcess<SampleApi.Program>());
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync(
            "configured chain",
            TestMethods.Placeholder,
            [new ApplicationAttribute("Api")]);

        var factory = context.TryServerFactory<SampleApi.Program>("Api");
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var entries = host.Trace.Snapshot().Entries!;
        var resolved = entries.Single(entry => entry.Kind == ProtoTargetTrace.Resolved);
        var skipped = entries.Single(entry => entry.Kind == ProtoTargetTrace.ProviderSkipped);
        Assert.Multiple(() =>
        {
            Assert.That(factory, Is.Null, "the configured winner registers no in-process server");
            Assert.That(host.HasCapability(ProtoCapabilityKinds.Server, "ASP.NET Core", "Api"), Is.False);
            Assert.That(host.HasCapability(ProtoCapabilityKinds.Clock, "Test clock", "Api"), Is.False);
            Assert.That(resolved.Attributes["environment.provider"], Is.EqualTo("configured"));
            Assert.That(skipped.Attributes["environment.provider"], Is.EqualTo("in-process"));
            Assert.That(
                skipped.Attributes["environment.reason"],
                Does.Contain("earlier provider 'configured'"),
                "the losing provider names the winner, not its own Always condition");
            Assert.That(
                entries.Any(entry => entry.Kind == "aspnetcore.server.initialize"),
                Is.False,
                "no server was initialized");
        });
    }

    [Test]
    public async Task LoopbackProvider_ShouldPublishTheAddressWithoutClaimingTheTestHost()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddApplication("Api", app => app
            .UseConfigured()
            .UseLoopback(SampleApi.Program.CreateApp)
            .AddRest(rest => rest.AddClient("Api")));
        builder.AddHttpReadiness("Api", "/ping");
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync(
            "loopback chain",
            TestMethods.Placeholder,
            [new ApplicationAttribute("Api")]);

        var address = ProtoApplication.BaseUrl(context, "Api");
        using var response = await context.Rest().GetAsync("/ping");
        var body = response.Content;
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var entities = host.Trace.Snapshot().Entities!;
        var readiness = entities.Single(entity => entity.Kind == "readiness");
        Assert.Multiple(() =>
        {
            Assert.That(address, Does.StartWith("http://127.0.0.1:"), "the loopback listener published its bound address");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Is.EqualTo("{\"message\":\"pong\"}"), "the client follows the published loopback address");
            Assert.That(
                host.HasCapability(ProtoCapabilityKinds.Server, "ASP.NET Core", "Api"),
                Is.False,
                "the loopback is a real process boundary, not the in-process test host");
            Assert.That(host.HasCapability(ProtoCapabilityKinds.Clock, "Test clock", "Api"), Is.False);
            Assert.That(
                readiness.State["readiness.url"],
                Does.Contain("127.0.0.1"),
                "readiness waits for the published address instead of claiming in-process");
        });
    }

    [Test]
    public async Task ConfiguredProvider_ShouldSkipTheLoopbackListener()
    {
        var created = false;
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:Api:BaseUrl"] = "http://127.0.0.1:9"
            }));
        builder.AddApplication("Api", app => app
            .UseConfigured()
            .UseLoopback(args =>
            {
                created = true;
                return SampleApi.Program.CreateApp(args);
            }));
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StopAsync();

        var entities = host.Trace.Snapshot().Entities!;
        var loopback = entities.Single(entity => entity.Id == "application:loopback:Api");
        Assert.Multiple(() =>
        {
            Assert.That(created, Is.False, "a losing loopback provider never builds its application");
            Assert.That(loopback.State["infrastructure.state"], Is.EqualTo("skipped"));
            Assert.That(
                loopback.State["infrastructure.reason"],
                Does.Contain("earlier provider 'configured'"),
                "the piece records why it did not start");
        });
    }
}
