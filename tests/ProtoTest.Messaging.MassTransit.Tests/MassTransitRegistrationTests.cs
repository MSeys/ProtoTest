namespace ProtoTest.Messaging.MassTransit.Tests;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.Messaging;
using ProtoTest.Messaging.MassTransit.TestApi;
using ProtoTest.TestSupport;

/// <summary>
/// Registration shape for the bridge: the adapter registers through the one messaging seam, so a
/// repeated call stays one broker, one capability and one run resource, and the first adapter
/// configured - with its own capability rule - wins.
/// </summary>
[TestFixture]
[Category("Characterization")]
public sealed class MassTransitRegistrationTests
{
    [Test]
    public async Task AddMessaging_CalledTwiceWithMassTransit_ShouldKeepOneRegistrationAndOneResource()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        IServiceCollection? services = null;
        builder.ConfigureServices(collection => services = collection);
        builder.AddAspNetCoreServer<Program>();
        builder.AddMessaging(messaging => messaging.UseMassTransit<Program>());
        builder.AddMessaging(messaging => messaging.UseMassTransit<Program>());

        await using var host = builder.Build();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                services!.Count(descriptor => descriptor.ServiceType == typeof(IProtoMessageBroker)),
                Is.EqualTo(1));
            Assert.That(host.HasCapability(ProtoCapabilityKinds.Broker), Is.True);
        }

        await host.StartAsync();
        var owned = host.Trace.Snapshot().Entries!
            .Where(entry => entry.Kind == "resource.owned")
            .Select(entry => entry.Attributes["resource.id"])
            .ToArray();
        Assert.That(owned, Is.EqualTo(new[] { "messaging:broker" }));

        var context = await host.StartTestAsync("masstransit idempotent", TestMethods.Placeholder);
        Assert.That(context.Service<IProtoMessageBroker>().Name, Is.EqualTo("MassTransit"));
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task AddMessaging_WhenAnAddressAdapterIsConfiguredFirst_ShouldKeepItAndItsCapabilityRule()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddAspNetCoreServer<Program>();
        builder.AddMessaging(messaging => messaging.UseBroker(_ => new StubBroker(), "ProtoTest:Fake:Address"));
        builder.AddMessaging(messaging => messaging.UseMassTransit<Program>());

        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("masstransit first adapter", TestMethods.Placeholder);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                context.Service<IProtoMessageBroker>().Name,
                Is.EqualTo("Stub"),
                "the first adapter configured wins");
            Assert.That(
                host.HasCapability(ProtoCapabilityKinds.Broker),
                Is.False,
                "the winner's address rule decides the capability; the losing bridge declares nothing");
        }

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task AddMessaging_WhenMassTransitIsConfiguredFirst_ShouldKeepItAndItsCapabilityRule()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddAspNetCoreServer<Program>();
        builder.AddMessaging(messaging => messaging.UseMassTransit<Program>());
        builder.AddMessaging(messaging => messaging.UseBroker(_ => new StubBroker(), "ProtoTest:Fake:Address"));

        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("masstransit first adapter reversed", TestMethods.Placeholder);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.Service<IProtoMessageBroker>().Name, Is.EqualTo("MassTransit"));
            Assert.That(
                host.HasCapability(ProtoCapabilityKinds.Broker),
                Is.True,
                "the in-process bridge keeps the capability while the application's BaseUrl is not configured");
        }

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    private sealed class StubBroker : IProtoMessageBroker
    {
        public string Name => "Stub";

        public ValueTask PublishAsync(ProtoMessage message, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask<IProtoMessageConsumer> CreateConsumerAsync(CancellationToken cancellationToken = default)
            => new(new StubConsumer());

        private sealed class StubConsumer : IProtoMessageConsumer
        {
            public ValueTask PrepareAsync(
                IReadOnlyCollection<string> destinations,
                CancellationToken cancellationToken = default)
                => ValueTask.CompletedTask;

            public ValueTask<ProtoMessage> AwaitAsync(
                string destination,
                Func<ProtoMessage, bool> predicate,
                TimeSpan timeout,
                CancellationToken cancellationToken = default)
                => throw new TimeoutException("The stub broker never has messages.");

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
