namespace ProtoTest.Messaging.RabbitMq.Tests;

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Messaging;

[TestFixture]
[Category("Characterization")]
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
        var context = await host.StartTestAsync("rabbitmq idempotent", TestMethods.Placeholder);
        Assert.That(
            context.Service<RabbitMqOptions>().ConnectionString,
            Is.EqualTo("amqp://127.0.0.1:1/"));
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task AddMessaging_WhenTheRabbitMqConfigureCallbackThrows_ShouldLeaveNoRegistrationBehind()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);

        Assert.Throws<InvalidOperationException>(
            () => builder.AddMessaging(messaging => messaging.UseRabbitMq(
                _ => throw new InvalidOperationException("bad options"))));

        // The throwing call ran the callback before registering anything, so a later call composes
        // from scratch instead of finding a half-applied registration.
        builder.AddMessaging(messaging => messaging.UseRabbitMq(
            options => options.ConnectionString = "amqp://127.0.0.1:1/"));
        await using var host = builder.Build();

        Assert.That(host.HasCapability(ProtoCapabilityKinds.Broker), Is.True);

        await host.StartAsync();
        var context = await host.StartTestAsync("rabbitmq throw path", TestMethods.Placeholder);
        Assert.That(
            context.Service<RabbitMqOptions>().ConnectionString,
            Is.EqualTo("amqp://127.0.0.1:1/"),
            "the later call's options are the ones the run resolves");
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }
}
