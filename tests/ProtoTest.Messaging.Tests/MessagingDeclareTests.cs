namespace ProtoTest.Messaging.Tests;

using Microsoft.Extensions.Configuration;
using ProtoTest.Core;

/// <summary>
/// Pins the declared-destination contract: <c>Declare</c> composes like <c>Tap</c>,
/// reaches the broker before any tap is prepared, fails loudly on an adapter that cannot declare, and
/// is an honest no-op on the in-memory broker, where every destination already exists.
/// </summary>
[TestFixture]
public sealed class MessagingDeclareTests
{
    [Test]
    public async Task Declare_ShouldAppendConfigurationDeclarationsAfterTheCodeOnes()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Messaging:DeclaredDestinations:0"] = "configured"
            }));
        builder.AddMessaging(messaging => messaging.Declare("alpha").Declare("beta", "alpha"));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("declare options", TestMethods.Placeholder);

        var options = context.Service<MessagingOptions>();

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(options.DeclaredDestinations, Is.EqualTo(new[] { "alpha", "beta", "configured" }),
            "repeated Declare calls compose and dedupe, then configuration binds over the code values");
    }

    [Test]
    public void Declare_ShouldRejectEmptyAndBlankDestinations()
    {
        var builder = new ProtoHostBuilder();

        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<ArgumentException>(() => builder.AddMessaging(messaging => messaging.Declare()));
            Assert.Throws<ArgumentException>(() => builder.AddMessaging(messaging => messaging.Declare(" ")));
            Assert.Throws<ArgumentNullException>(() => builder.AddMessaging(messaging => messaging.Declare(null!)));
        }
    }

    [Test]
    public async Task Declare_ShouldReachTheBrokerBeforeAnyTapIsPrepared()
    {
        var broker = new RecordingBroker();
        var builder = new ProtoHostBuilder();
        builder.AddMessaging(messaging => messaging
            .UseBroker(_ => broker)
            .Declare("owned", "idle")
            .Tap("owned"));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("declare prepare order", TestMethods.Placeholder);

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(broker.Calls, Is.EqualTo(new[] { "declare:owned,idle", "prepare:owned" }),
            "the setup hook declares the suite-owned destinations first - including ones no test taps - " +
            "so a tap binds to an exchange that already exists");
    }

    [Test]
    public async Task Declare_WhenTheAdapterDoesNotDeclareDestinations_ShouldFailSetupNamingTheAdapter()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddMessaging(messaging => messaging
            .UseBroker(_ => new NoDeclareBroker())
            .Declare("owned"));
        await using var host = builder.Build();
        await host.StartAsync();

        // The default DeclareAsync refuses instead of pretending: the test must not run and publish
        // into a destination nothing created.
        var exception = Assert.ThrowsAsync<NotSupportedException>(async () =>
            await host.StartTestAsync("declare unsupported", TestMethods.Placeholder));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("NoDeclare"));
            Assert.That(exception.Message, Does.Contain("DeclareAsync"));
        });
        await host.StopAsync();
    }

    [Test]
    public async Task Declare_OnTheInMemoryBroker_ShouldBeANoOpThatKeepsTheRoundTripWorking()
    {
        var builder = new ProtoHostBuilder();
        builder.AddMessaging(messaging => messaging.Declare("owned").Tap("owned"));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("declare in memory", TestMethods.Placeholder);
        var messages = context.Messaging();

        // The in-memory broker has no topology, so the declaration completes as a no-op; its real
        // semantics - every destination exists, publish creates history and await reads it - are
        // what the suite gets, declared or not.
        await context.Service<IProtoMessageBroker>().DeclareAsync(["anything"]);
        await messages.PublishAsync("owned", """{"id":1}""");
        var declared = await messages.AwaitAsync(
            "owned",
            message => message.Payload == """{"id":1}""",
            TimeSpan.FromSeconds(1));
        await messages.PublishAsync("undeclared", """{"id":2}""");
        var undeclared = await messages.AwaitAsync(
            "undeclared",
            message => message.Payload == """{"id":2}""",
            TimeSpan.FromSeconds(1));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.Multiple(() =>
        {
            Assert.That(declared.Payload, Is.EqualTo("""{"id":1}"""));
            Assert.That(undeclared.Payload, Is.EqualTo("""{"id":2}"""),
                "a declaration adds nothing in memory: an undeclared destination behaves the same way");
        });
    }

    private sealed class RecordingBroker : IProtoMessageBroker
    {
        public string Name => "Recording";

        public List<string> Calls { get; } = [];

        public ValueTask PublishAsync(ProtoMessage message, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask<IProtoMessageConsumer> CreateConsumerAsync(CancellationToken cancellationToken = default)
            => new(new RecordingConsumer(this));

        public ValueTask DeclareAsync(
            IReadOnlyCollection<string> destinations,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"declare:{string.Join(",", destinations)}");
            return ValueTask.CompletedTask;
        }

        private sealed class RecordingConsumer(RecordingBroker owner) : IProtoMessageConsumer
        {
            public ValueTask PrepareAsync(
                IReadOnlyCollection<string> destinations,
                CancellationToken cancellationToken = default)
            {
                owner.Calls.Add($"prepare:{string.Join(",", destinations)}");
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

    /// <summary>An adapter that takes the interface default: it can publish and await, not declare.</summary>
    private sealed class NoDeclareBroker : IProtoMessageBroker
    {
        public string Name => "NoDeclare";

        public ValueTask PublishAsync(ProtoMessage message, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask<IProtoMessageConsumer> CreateConsumerAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The no-declare broker never creates a consumer.");
    }
}
