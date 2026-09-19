namespace ProtoTest.Messaging.RabbitMq.Tests;

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ProtoTest.Core;
using ProtoTest.Messaging;
using System.Reflection;

[TestFixture]
public sealed class RegistrationIdempotencyTests
{
    [Test]
    public async Task AddMessaging_CalledTwiceWithRabbitMq_ShouldKeepTheFirstOptionsAndOneResource()
    {
        var builder = new ProtoHostBuilder();
        IServiceCollection? services = null;
        builder.ConfigureServices(collection => services = collection);
        builder.AddMessaging(messaging => messaging.UseRabbitMq(
            options => options.ConnectionString = "amqp://127.0.0.1:1/"));
        builder.AddMessaging(messaging => messaging.UseRabbitMq());

        await using var host = builder.Build();
        Assert.Multiple(() =>
        {
            Assert.That(
                services!.Count(descriptor => descriptor.ServiceType == typeof(RabbitMqOptions)),
                Is.EqualTo(1));
            Assert.That(
                services!.Count(descriptor => descriptor.ServiceType == typeof(IProtoClientInitializer)),
                Is.EqualTo(1));
            Assert.That(
                services!.Count(descriptor => descriptor.ServiceType == typeof(ProtoCapabilityDescriptor)),
                Is.EqualTo(1));
            Assert.That(host.HasCapability(ProtoCapabilityKinds.Broker), Is.True);
        });

        await host.StartAsync();
        var owned = host.Trace.Snapshot().Entries!
            .Where(entry => entry.Kind == "resource.owned")
            .Select(entry => entry.Attributes["resource.id"])
            .ToArray();
        Assert.That(owned, Is.EqualTo(new[] { "messaging:broker" }));

        // No publish happens, so no connection is attempted; the first call's options must still win.
        var context = await host.StartTestAsync("rabbitmq idempotent", TestMethod());
        Assert.That(
            context.Service<RabbitMqOptions>().ConnectionString,
            Is.EqualTo("amqp://127.0.0.1:1/"));
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
