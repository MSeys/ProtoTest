namespace ProtoTest.Web.Tests;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using System.Reflection;

[TestFixture]
public sealed class RegistrationIdempotencyTests
{
    [Test]
    public async Task AddWeb_PlaywrightCalledTwice_ShouldRegisterOneBackendAndRun()
    {
        var builder = new ProtoHostBuilder();
        IServiceCollection? services = null;
        builder.ConfigureServices(collection => services = collection);
        builder.AddWeb();
        builder.AddWeb();

        await using var host = builder.Build();
        Assert.Multiple(() =>
        {
            Assert.That(services!.Count(descriptor => descriptor.ServiceType == typeof(IWebBackendFactory)), Is.EqualTo(1));
            Assert.That(services!.Count(descriptor => descriptor.ServiceType == typeof(IProtoCollector)), Is.EqualTo(1));
            Assert.That(
                services!.Count(descriptor => descriptor.ServiceType == typeof(ProtoCapabilityDescriptor)),
                Is.EqualTo(1));
        });

        await host.StartAsync();
        await host.StartTestAsync("playwright idempotent", TestMethod());
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task AddWeb_SeleniumCalledTwice_ShouldRegisterOneBackendAndRun()
    {
        var builder = new ProtoHostBuilder();
        IServiceCollection? services = null;
        builder.ConfigureServices(collection => services = collection);
        builder.AddWeb(static () => throw new InvalidOperationException("No session is created, so no driver."));
        builder.AddWeb(static () => throw new InvalidOperationException("The second driver factory must not register."));

        await using var host = builder.Build();
        Assert.Multiple(() =>
        {
            Assert.That(services!.Count(descriptor => descriptor.ServiceType == typeof(IWebBackendFactory)), Is.EqualTo(1));
            Assert.That(services!.Count(descriptor => descriptor.ServiceType == typeof(IProtoCollector)), Is.EqualTo(1));
            Assert.That(
                services!.Count(descriptor => descriptor.ServiceType == typeof(ProtoCapabilityDescriptor)),
                Is.EqualTo(1));
        });

        await host.StartAsync();
        await host.StartTestAsync("selenium idempotent", TestMethod());
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task AddWeb_UnderAnApplicationCalledTwice_ShouldRegisterOneBackendAndClient()
    {
        var builder = new ProtoHostBuilder();
        IServiceCollection? services = null;
        builder.ConfigureServices(collection => services = collection);
        builder.AddApplication("Api", app =>
        {
            app.AddWeb();
            app.AddWeb();
        });

        Assert.Multiple(() =>
        {
            Assert.That(services!.Count(descriptor => descriptor.ServiceType == typeof(IWebBackendFactory)), Is.EqualTo(1));
            Assert.That(services!.Count(descriptor => descriptor.ServiceType == typeof(IProtoRunHook)), Is.EqualTo(1));
            Assert.That(
                services!.Count(descriptor => descriptor.ServiceType == typeof(ProtoCapabilityDescriptor)),
                Is.EqualTo(1));
        });

        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("playwright application idempotent", TestMethod());
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task AddWeb_SeleniumUnderAnApplicationCalledTwice_ShouldRegisterOneClient()
    {
        var builder = new ProtoHostBuilder();
        builder.AddApplication("Api", app =>
        {
            app.AddWeb(static () => throw new InvalidOperationException("No session is created, so no driver."));
            app.AddWeb(static () => throw new InvalidOperationException("The second driver factory must not register."));
        });

        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("selenium application idempotent", TestMethod());

        var application = context.Service<ProtoApplicationClients>();
        Assert.That(application.Clients.Count(client => client.ProtocolName == "Web"), Is.EqualTo(1));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public void AddWeb_CalledTwice_ShouldRunBothConfigureCallbacksAndRegisterInfrastructureOnce()
    {
        var configureCalls = 0;
        var builder = new ProtoHostBuilder();
        IServiceCollection? services = null;
        builder.ConfigureServices(collection => services = collection);
        builder.AddWeb(_ => configureCalls++);
        builder.AddWeb(_ => configureCalls++);

        Assert.Multiple(() =>
        {
            Assert.That(configureCalls, Is.EqualTo(2), "every call's configure callback runs");
            Assert.That(services!.Count(descriptor => descriptor.ServiceType == typeof(IWebBackendFactory)), Is.EqualTo(1));
            Assert.That(services!.Count(descriptor => descriptor.ServiceType == typeof(IProtoCollector)), Is.EqualTo(1));
            Assert.That(
                services!.Count(descriptor => descriptor.ServiceType == typeof(ProtoCapabilityDescriptor)),
                Is.EqualTo(1));
        });
    }

    [Test]
    public void AddWeb_WhenConfigureThrows_ShouldNotPreventALaterSuccessfulCall()
    {
        var builder = new ProtoHostBuilder();
        IServiceCollection? services = null;
        builder.ConfigureServices(collection => services = collection);

        Assert.Throws<InvalidOperationException>(() =>
            builder.AddWeb(_ => throw new InvalidOperationException("configure exploded")));
        builder.AddWeb();

        Assert.Multiple(() =>
        {
            Assert.That(services!.Count(descriptor => descriptor.ServiceType == typeof(IWebBackendFactory)), Is.EqualTo(1));
            Assert.That(services!.Count(descriptor => descriptor.ServiceType == typeof(IProtoCollector)), Is.EqualTo(1));
            Assert.That(
                services!.Count(descriptor => descriptor.ServiceType == typeof(ProtoCapabilityDescriptor)),
                Is.EqualTo(1));
        });
    }

    [Test]
    public void AddWeb_UnderAnApplicationCalledTwice_ShouldRunBothConfigureCallbacks()
    {
        var configureCalls = 0;
        var builder = new ProtoHostBuilder();
        builder.AddApplication("Api", app =>
        {
            app.AddWeb(_ => configureCalls++);
            app.AddWeb(_ => configureCalls++);
        });

        Assert.That(configureCalls, Is.EqualTo(2), "every call's configure callback runs");
    }

    private static MethodInfo TestMethod()
        => typeof(RegistrationIdempotencyTests).GetMethod(
            nameof(Placeholder), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static void Placeholder()
    {
    }
}
