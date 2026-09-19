namespace ProtoTest.AspNetCore.Tests;

using System.Net;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

[TestFixture]
public sealed class RegistrationIdempotencyTests
{
    [Test]
    public async Task AddAspNetCoreServer_CalledTwice_ShouldRegisterOneServerAndRun()
    {
        var builder = new ProtoHostBuilder();
        IServiceCollection? services = null;
        builder.ConfigureServices(collection => services = collection);
        builder.AddAspNetCoreServer<SampleApi.Program>("Default");
        builder.AddAspNetCoreServer<SampleApi.Program>("Default");

        await using var host = builder.Build();
        Assert.Multiple(() =>
        {
            Assert.That(
                services!.Count(descriptor => descriptor.ServiceType == typeof(IProtoClientInitializer)),
                Is.EqualTo(1));
            Assert.That(
                services!.Count(descriptor => descriptor.ServiceType == typeof(ProtoCapabilityDescriptor)),
                Is.EqualTo(1));
        });

        await host.StartAsync();
        await host.StartTestAsync("aspnet idempotent", (MethodInfo)MethodInfo.GetCurrentMethod()!);

        var client = Proto.Context.Client<HttpClient>("Default");
        var response = await client.GetAsync("/ping");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task AddAspNetCoreServer_UnderAnApplicationCalledTwice_ShouldKeepOneTransportAndComposeServers()
    {
        var builder = new ProtoHostBuilder();
        IServiceCollection? services = null;
        builder.ConfigureServices(collection => services = collection);
        builder.AddApplication("Api", app =>
        {
            app.AddAspNetCoreServer<SampleApi.Program>();
            app.AddAspNetCoreServer<SampleApi.Program>();
        });

        await using var host = builder.Build();
        Assert.Multiple(() =>
        {
            Assert.That(
                services!.Count(descriptor => descriptor.ServiceType == typeof(ProtoApplicationTransport)),
                Is.EqualTo(1),
                "the application transport is infrastructure and registers once");
            Assert.That(
                services!.Count(descriptor => descriptor.ServiceType == typeof(IProtoClientInitializer)),
                Is.EqualTo(2),
                "each call registers its server; the client initializer hook keeps the first that initializes");
        });

        await host.StartAsync();
        await host.StopAsync();
    }
}
