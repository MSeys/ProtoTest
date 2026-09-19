namespace ProtoTest.Rest.Tests;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ProtoTest.Core;
using ProtoTest.Http;
using System.Net.Http;
using System.Reflection;

[TestFixture]
public sealed class RegistrationIdempotencyTests
{
    [Test]
    public async Task AddRest_CalledTwice_ShouldComposeClientsAndRegisterInfrastructureOnce()
    {
        var builder = new ProtoHostBuilder();
        IServiceCollection? services = null;
        builder.ConfigureServices(collection => services = collection);
        builder.AddRest(rest => rest.AddClient("Orders", "https://api.test"));
        builder.AddRest(rest => rest.AddClient("Other", "https://other.test"));
        builder.AddRest();

        Assert.Multiple(() =>
        {
            Assert.That(
                services!.Count(descriptor => descriptor.ServiceType == typeof(IProtoClientInitializer)),
                Is.EqualTo(2));
            Assert.That(services!.Count(descriptor => descriptor.ServiceType == typeof(IProtoTestHook)), Is.EqualTo(1));
            Assert.That(
                services!.Count(descriptor => descriptor.ServiceType == typeof(ProtoCapabilityDescriptor)),
                Is.EqualTo(1));
            Assert.That(
                services!.Count(descriptor => descriptor.ServiceType == typeof(ProtoHttpResponseOptions)
                    && descriptor.IsKeyedService
                    && Equals(descriptor.ServiceKey, ProtoRestBuilder.ProtocolName)),
                Is.EqualTo(1));
        });

        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("rest idempotent", TestMethod());

        Assert.Multiple(() =>
        {
            Assert.That(context.Client<HttpClient>("Orders").BaseAddress, Is.EqualTo(new Uri("https://api.test")));
            Assert.That(context.Client<HttpClient>("Other").BaseAddress, Is.EqualTo(new Uri("https://other.test")));
        });
        context.Rest("Orders");
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task AddRest_WithTheSameClientNameTwice_ShouldKeepTheFirstWorkingRegistration()
    {
        var builder = new ProtoHostBuilder();
        builder.AddRest(rest => rest.AddClient("Orders", "https://first.test"));
        builder.AddRest(rest => rest.AddClient("Orders", "https://second.test"));

        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("rest duplicate client", TestMethod());

        Assert.That(context.Client<HttpClient>("Orders").BaseAddress, Is.EqualTo(new Uri("https://first.test")));
        context.Rest("Orders");
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public void AddCollector_CalledTwiceForTheSameTarget_ShouldRegisterOnce()
    {
        var builder = new ProtoHostBuilder();
        IServiceCollection? services = null;
        builder.ConfigureServices(collection => services = collection);
        builder.AddRest(rest => rest
            .AddClient("Orders", "https://api.test")
            .AddCollector<RestCoverageCollector>()
            .AddCollector<RestCoverageCollector>());

        Assert.That(
            services!.Count(descriptor => descriptor.ServiceType == typeof(IProtoCollector)),
            Is.EqualTo(1));
    }

    [Test]
    public void AddCollector_ForDifferentTargets_ShouldRegisterEach()
    {
        var builder = new ProtoHostBuilder();
        IServiceCollection? services = null;
        builder.ConfigureServices(collection => services = collection);
        builder.AddRest(rest =>
        {
            rest.AddClient("Orders", "https://api.test").AddCollector<RestCoverageCollector>();
            rest.AddClient("Other", "https://other.test").AddCollector<RestCoverageCollector>();
        });

        Assert.That(
            services!.Count(descriptor => descriptor.ServiceType == typeof(IProtoCollector)),
            Is.EqualTo(2));
    }

    [Test]
    public void AddRest_WhenConfigureThrows_ShouldNotPreventALaterSuccessfulCall()
    {
        var builder = new ProtoHostBuilder();
        IServiceCollection? services = null;
        builder.ConfigureServices(collection => services = collection);

        Assert.Throws<InvalidOperationException>(() =>
            builder.AddRest(_ => throw new InvalidOperationException("configure exploded")));
        builder.AddRest(rest => rest.AddClient("Orders", "https://api.test"));

        Assert.Multiple(() =>
        {
            Assert.That(services!.Count(descriptor => descriptor.ServiceType == typeof(IProtoTestHook)), Is.EqualTo(1));
            Assert.That(
                services!.Count(descriptor => descriptor.ServiceType == typeof(IProtoClientInitializer)),
                Is.EqualTo(1));
            Assert.That(
                services!.Count(descriptor => descriptor.ServiceType == typeof(ProtoCapabilityDescriptor)),
                Is.EqualTo(1));
        });
    }

    [Test]
    public async Task AddRest_UnderAnApplicationCalledTwice_ShouldComposeClients()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:ControlPlane:BaseUrl"] = "https://app.test"
            }));
        builder.AddApplication("ControlPlane", app =>
        {
            app.AddRest(rest => rest.AddClient("Orders"));
            app.AddRest(rest => rest.AddClient("Billing"));
        });

        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync(
            "rest application idempotent",
            TestMethod(),
            [new ApplicationAttribute("ControlPlane")]);

        Assert.Multiple(() =>
        {
            Assert.That(
                context.Client<HttpClient>("ControlPlane:Orders").BaseAddress,
                Is.EqualTo(new Uri("https://app.test/")));
            Assert.That(
                context.Client<HttpClient>("ControlPlane:Billing").BaseAddress,
                Is.EqualTo(new Uri("https://app.test/")));
        });
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task ConfigureResponsesAndCaptureAttachments_CalledTwice_ShouldComposeBothCallbacks()
    {
        var builder = new ProtoHostBuilder();
        builder.AddRest(rest =>
        {
            rest.AddClient("Orders", "https://api.test");
            rest.ConfigureResponses(options => options.MaxResponseBodyBytes = 2048);
            rest.CaptureAttachments(options => options.CaptureResponses = false);
        });
        builder.AddRest(rest =>
        {
            rest.ConfigureResponses(options => options.MaxDiagnosticBodyLength = 64);
            rest.CaptureAttachments(options => options.MaxDiagnosticBodyLength = 128);
        });

        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("rest options compose", TestMethod());

        var responses = context.ResolveResponseOptions(ProtoRestBuilder.ProtocolName);
        var attachments = context.ResolveAttachmentOptions(ProtoRestBuilder.ProtocolName);

        Assert.Multiple(() =>
        {
            Assert.That(responses.MaxResponseBodyBytes, Is.EqualTo(2048));
            Assert.That(responses.MaxDiagnosticBodyLength, Is.EqualTo(64));
            Assert.That(attachments, Is.Not.Null);
            Assert.That(attachments!.CaptureResponses, Is.False);
            Assert.That(attachments.MaxDiagnosticBodyLength, Is.EqualTo(128));
        });
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    private static MethodInfo TestMethod()
        => typeof(RegistrationIdempotencyTests).GetMethod(
            nameof(Placeholder), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static void Placeholder()
    {
    }
}
