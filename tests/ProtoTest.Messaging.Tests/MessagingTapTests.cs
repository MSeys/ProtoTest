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
        Assert.That(broker.Prepared, Is.EquivalentTo(new[] { "shared", "own" }),
            "each declared destination is prepared exactly once, deduped and without blanks; the setup " +
            "hook prepares them concurrently, so each destination's outcome is recorded on its own");
    }

    [Test]
    public async Task Tap_ShouldPrepareDestinationsConcurrently()
    {
        // A broker that holds every prepare until it has seen them all: with a serial setup loop the
        // first prepare waits out the barrier and the destinations never overlap.
        var broker = new BarrierBroker(participants: 2, TimeSpan.FromSeconds(5));
        var builder = new ProtoHostBuilder();
        builder.AddMessaging(messaging => messaging.UseBroker(_ => broker).Tap("alpha", "beta"));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("tap concurrent prepare", TestMethods.Placeholder);

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(broker.AllInFlight, Is.True,
            "the setup hook prepares the declared destinations concurrently: both are in flight at once");
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
        private readonly object _lock = new();

        public string Name => "Recording";

        public IReadOnlyList<string> Prepared { get; private set; } = [];

        public ValueTask PublishAsync(ProtoMessage message, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask<IProtoMessageConsumer> CreateConsumerAsync(CancellationToken cancellationToken = default)
            => new(new RecordingConsumer(this));

        private void Record(IEnumerable<string> destinations)
        {
            lock (_lock)
            {
                // Accumulate: the setup hook prepares one destination per call so a failure can be
                // attributed; the calls run concurrently, so the recording takes the lock.
                Prepared = [.. Prepared, .. destinations];
            }
        }

        private sealed class RecordingConsumer(RecordingBroker owner) : IProtoMessageConsumer
        {
            public ValueTask PrepareAsync(
                IReadOnlyCollection<string> destinations,
                CancellationToken cancellationToken = default)
            {
                owner.Record(destinations);
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

    /// <summary>
    /// A broker whose prepares hold until every expected participant is in flight: a serial setup loop
    /// cannot reach the barrier, while concurrent prepares complete it.
    /// </summary>
    private sealed class BarrierBroker(int participants, TimeSpan timeout) : IProtoMessageBroker
    {
        private readonly int _participants = participants;
        private readonly TimeSpan _timeout = timeout;
        private readonly TaskCompletionSource _allInFlight = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _active;

        public string Name => "Barrier";

        /// <summary>Set once every expected prepare is in flight at the same time.</summary>
        public bool AllInFlight { get; private set; }

        public ValueTask PublishAsync(ProtoMessage message, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask<IProtoMessageConsumer> CreateConsumerAsync(CancellationToken cancellationToken = default)
            => new(new BarrierConsumer(this));

        private sealed class BarrierConsumer(BarrierBroker owner) : IProtoMessageConsumer
        {
            public async ValueTask PrepareAsync(
                IReadOnlyCollection<string> destinations,
                CancellationToken cancellationToken = default)
            {
                var active = Interlocked.Increment(ref owner._active);
                if (active >= owner._participants)
                {
                    owner.AllInFlight = true;
                    owner._allInFlight.TrySetResult();
                }
                else
                {
                    await Task.WhenAny(owner._allInFlight.Task, Task.Delay(owner._timeout));
                }

                Interlocked.Decrement(ref owner._active);
            }

            public ValueTask<ProtoMessage> AwaitAsync(
                string destination,
                Func<ProtoMessage, bool> predicate,
                TimeSpan timeout,
                CancellationToken cancellationToken = default)
                => throw new TimeoutException("The barrier broker never has messages.");

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
