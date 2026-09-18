namespace ProtoTest.Rest.Tests;

using System.Net.Http;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using ProtoTest.Core;
using NUnit.Framework;

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
            rest.AddClient("Orders");
            rest.AddClient("Billing");
        }));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync(
            "rest application", TestMethod(), [new ApplicationAttribute("ControlPlane", "Rest:Billing")]);

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
        builder.AddApplication("ControlPlane", app => app.AddRest(rest => rest.AddClient("Orders")));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("rest application", TestMethod(), [new ApplicationAttribute("ControlPlane")]);

        context.Rest("Orders");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var client = host.Trace.Snapshot().Tests.Single().Entities!
            .Single(entity => entity.Kind == ProtoTraceEntityKinds.Client
                && entity.Id.EndsWith("ControlPlane:Orders", StringComparison.Ordinal));
        Assert.That(client.Versions, Is.Not.Empty);
    }

    private static MethodInfo TestMethod()
        => typeof(ApplicationRestTests).GetMethod(nameof(Placeholder), BindingFlags.Static | BindingFlags.NonPublic)!;

    private static void Placeholder()
    {
    }
}
