namespace ProtoTest.Grpc.Tests;

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ProtoTest.Core;
using System.Reflection;

[TestFixture]
public sealed class RegistrationIdempotencyTests
{
    [Test]
    public async Task AddGrpc_CalledTwice_ShouldComposeClientsAndRegisterInfrastructureOnce()
    {
        var builder = new ProtoHostBuilder();
        IServiceCollection? services = null;
        builder.ConfigureServices(collection => services = collection);
        builder.AddGrpc(grpc => grpc.AddClient("Echo", "http://127.0.0.1:1"));
        builder.AddGrpc(grpc => grpc.AddClient("Other", "http://127.0.0.1:2"));
        builder.AddGrpc();

        Assert.Multiple(() =>
        {
            Assert.That(
                services!.Count(descriptor => descriptor.ServiceType == typeof(IProtoClientInitializer)),
                Is.EqualTo(2));
            Assert.That(services!.Count(descriptor => descriptor.ServiceType == typeof(IProtoTestHook)), Is.EqualTo(1));
            Assert.That(
                services!.Count(descriptor => descriptor.ServiceType == typeof(ProtoCapabilityDescriptor)),
                Is.EqualTo(1));
        });

        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc idempotent", TestMethod());

        Assert.Multiple(() =>
        {
            Assert.That(context.TryClient<ProtoGrpcClient>("Echo"), Is.Not.Null);
            Assert.That(context.TryClient<ProtoGrpcClient>("Other"), Is.Not.Null);
        });
        _ = context.Grpc("Echo");
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public void AddGrpc_WhenConfigureThrows_ShouldNotPreventALaterSuccessfulCall()
    {
        var builder = new ProtoHostBuilder();
        IServiceCollection? services = null;
        builder.ConfigureServices(collection => services = collection);

        Assert.Throws<InvalidOperationException>(() =>
            builder.AddGrpc(_ => throw new InvalidOperationException("configure exploded")));
        builder.AddGrpc(grpc => grpc.AddClient("Echo", "http://127.0.0.1:1"));

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

    private static MethodInfo TestMethod()
        => typeof(RegistrationIdempotencyTests).GetMethod(
            nameof(Placeholder), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static void Placeholder()
    {
    }
}
