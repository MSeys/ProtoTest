namespace ProtoTest.Core.Tests;

using System.Reflection;
using Microsoft.Extensions.Configuration;

[TestFixture]
public sealed class ProtoApplicationTests
{
    [Test]
    public async Task Application_ShouldResolveDefaultBoundAndExplicitClients()
    {
        var builder = new ProtoHostBuilder();
        builder.AddApplication("ControlPlane", app =>
        {
            app.RegisterClient("Rest", "Orders");
            app.RegisterClient("Rest", "Billing");
            app.RegisterClient("GraphQL", "ControlPlane");
        });
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync(
            "application", TestMethods.Placeholder, [new ApplicationAttribute("ControlPlane", "Rest:Billing")]);

        Assert.Multiple(() =>
        {
            Assert.That(context.Service<ProtoApplicationRegistry>().Clients("ControlPlane", "Rest"),
                Is.EqualTo(new[] { "Orders", "Billing" }));
            Assert.That(context.Resolve<ProtoApplicationState>().ApplicationName, Is.EqualTo("ControlPlane"));
            Assert.That(ProtoApplicationResolution.ResolveClientName(context, "Rest"), Is.EqualTo("Billing"),
                "the [Application] binding wins over the default");
            Assert.That(ProtoApplicationResolution.ResolveClientName(context, "GraphQL"), Is.EqualTo("ControlPlane"),
                "an unbound protocol uses the first registered client");
            Assert.That(ProtoApplicationResolution.ResolveClientName(context, "Rest", requested: "Orders"),
                Is.EqualTo("Orders"), "an explicit call-site name wins over everything");
        });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public void Application_ShouldRejectMalformedBindings()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => new ApplicationAttribute("ControlPlane", "Billing"));
            Assert.Throws<ArgumentException>(() => new ApplicationAttribute("ControlPlane", "Rest:"));
            Assert.Throws<ArgumentException>(() => new ApplicationAttribute(""));
        });
    }

    [Test]
    public void Application_ShouldRejectWhitespaceProtocolsAndClients()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => new ApplicationAttribute("ControlPlane", "Rest:   "));
            Assert.Throws<ArgumentException>(() => new ApplicationAttribute("ControlPlane", "   :Billing"));
        });

        var attribute = new ApplicationAttribute("ControlPlane", "  Rest  :  Billing  ");
        Assert.That(attribute.Bindings, Is.EqualTo(new Dictionary<string, string> { ["Rest"] = "Billing" }));
    }

    [Test]
    public async Task Resolution_ShouldFailWhenTheApplicationHasNoSuchProtocol()
    {
        var builder = new ProtoHostBuilder();
        builder.AddApplication("ControlPlane", app => app.RegisterClient("Rest", "Orders"));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("application", TestMethods.Placeholder, [new ApplicationAttribute("ControlPlane")]);

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProtoApplicationResolution.ResolveClientName(context, "GraphQL"));
        Assert.That(exception!.Message, Does.Contain("GraphQL"));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task Resolution_WhenNoApplicationStateWasSet_ShouldReportTheMissingSelection()
    {
        // Audit 5 A5-14: the attribute's BeforeTestAsync is the one state factory. A context without
        // state never ran an [Application] attribute, so the documented error is reported instead of
        // the attribute being read a second time off the method.
        var builder = new ProtoHostBuilder();
        builder.AddApplication("ControlPlane", app => app.RegisterClient("Rest", "Orders"));
        await using var host = builder.Build();
        await host.StartAsync();
        var method = typeof(ProtoApplicationTests).GetMethod(
            nameof(ApplicationBoundMethod), BindingFlags.NonPublic | BindingFlags.Static)!;
        var context = await host.StartTestAsync("state-less", "00001", method);

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProtoApplicationResolution.ResolveClientName(context, "Rest"));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("No application is selected"));
            Assert.That(
                ProtoApplicationResolution.ResolveState(context),
                Is.Null,
                "the attribute on the method is not a second source of state");
            Assert.That(ProtoApplicationResolution.ResolveApplicationName(context), Is.Null);
        });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public void ResolveSetting_ShouldPreferPublishedSettingsAndFallBackToConfiguration()
    {
        // Audit 5 A5-25: the sample reads a container-published connection string through the one
        // precedence helper instead of re-implementing it.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Northstar"] = "from-configuration"
            })
            .Build();
        var settings = new ProtoInfrastructureSettings();
        settings.Set("ConnectionStrings:Northstar", "from-infrastructure");

        Assert.Multiple(() =>
        {
            Assert.That(
                ProtoApplication.ResolveSetting(configuration, settings, "ConnectionStrings:Northstar"),
                Is.EqualTo("from-infrastructure"),
                "a value a started piece published wins over static configuration");
            Assert.That(
                ProtoApplication.ResolveSetting(configuration, settings: null, "ConnectionStrings:Northstar"),
                Is.EqualTo("from-configuration"),
                "without a published value the static configuration answers");
            Assert.That(
                ProtoApplication.ResolveSetting(configuration, settings, "Missing"),
                Is.Null,
                "neither source has the key");
        });
    }

    [Test]
    public void ResolveSetting_ShouldIgnoreABlankPublishedValue()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Key"] = "configured" })
            .Build();
        var settings = new ProtoInfrastructureSettings();
        settings.Set("Key", "   ");

        Assert.That(
            ProtoApplication.ResolveSetting(configuration, settings, "Key"),
            Is.EqualTo("configured"),
            "a blank published value is not an address");
    }

    [Application("ControlPlane", "Rest:Orders")]
    private static void ApplicationBoundMethod()
    {
    }
}
