namespace ProtoTest.Messaging.Tests;

using System.Reflection;
using Microsoft.Extensions.Configuration;
using ProtoTest.Core;

[TestFixture]
public sealed class MessagingTapTests
{
    [Test]
    public async Task Tap_ShouldAppendConfigurationDestinationsAfterTheCodeOnes()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Messaging:Destinations:0"] = "configured"
            }));
        builder.AddMessaging(messaging => messaging.Tap("alpha").Tap("beta", "alpha"));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("tap options", TestMethods.Placeholder);

        var options = context.Service<MessagingOptions>();

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(options.Destinations, Is.EqualTo(new[] { "alpha", "beta", "configured" }),
            "repeated Tap calls compose and dedupe, then configuration binds over the code values");
    }

    [Test]
    public async Task Tap_ShouldPrepareEachDestinationOnceAcrossCodeAndConfiguration()
    {
        var broker = new RecordingBroker();
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Messaging:Destinations:0"] = "shared",
                ["ProtoTest:Messaging:Destinations:1"] = " "
            }));
        builder.AddMessaging(messaging => messaging.UseBroker(_ => broker).Tap("shared", "own").Tap("own"));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("tap prepare", TestMethods.Placeholder);

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(broker.Prepared, Is.EqualTo(new[] { "shared", "own" }),
            "each declared destination is prepared exactly once, deduped and without blanks; " +
            "the setup hook prepares them one at a time so a destination that cannot be declared " +
            "does not fail the whole class");
    }

    [Test]
    public void Tap_ShouldRejectEmptyAndBlankDestinations()
    {
        var builder = new ProtoHostBuilder();

        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<ArgumentException>(() => builder.AddMessaging(messaging => messaging.Tap()));
            Assert.Throws<ArgumentException>(() => builder.AddMessaging(messaging => messaging.Tap(" ")));
            Assert.Throws<ArgumentNullException>(() => builder.AddMessaging(messaging => messaging.Tap(null!)));
        }
    }

    private sealed class RecordingBroker : IProtoMessageBroker
    {
        public string Name => "Recording";

        public IReadOnlyList<string> Prepared { get; private set; } = [];

        public ValueTask PublishAsync(ProtoMessage message, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask<IProtoMessageConsumer> CreateConsumerAsync(CancellationToken cancellationToken = default)
            => new(new RecordingConsumer(this));

        private sealed class RecordingConsumer(RecordingBroker owner) : IProtoMessageConsumer
        {
            public ValueTask PrepareAsync(
                IReadOnlyCollection<string> destinations,
                CancellationToken cancellationToken = default)
            {
                // Accumulate: the setup hook prepares one destination per call so a failure can be
                // attributed, and this records the full declaration order either way.
                owner.Prepared = [.. owner.Prepared, .. destinations];
                return ValueTask.CompletedTask;
            }

            public ValueTask<ProtoMessage> AwaitAsync(
                string destination,
                Func<ProtoMessage, bool> predicate,
                TimeSpan timeout,
                CancellationToken cancellationToken = default)
                => throw new TimeoutException("The recording broker never has messages.");

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
