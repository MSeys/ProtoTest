namespace ProtoTest.Core.Tests;

using System.Reflection;
using NUnit.Framework;

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
            "application", TestMethod(), [new ApplicationAttribute("ControlPlane", "Rest:Billing")]);

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
        var context = await host.StartTestAsync("application", TestMethod(), [new ApplicationAttribute("ControlPlane")]);

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProtoApplicationResolution.ResolveClientName(context, "GraphQL"));
        Assert.That(exception!.Message, Does.Contain("GraphQL"));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    private static MethodInfo TestMethod()
        => typeof(ProtoApplicationTests).GetMethod(nameof(Placeholder), BindingFlags.Static | BindingFlags.NonPublic)!;

    private static void Placeholder()
    {
    }
}
