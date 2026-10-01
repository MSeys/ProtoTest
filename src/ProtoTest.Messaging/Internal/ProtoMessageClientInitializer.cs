namespace ProtoTest.Messaging.Internal;

using ProtoTest.Core;

/// <summary>
/// Creates the test's message client and its consumer during setup and registers both with the test, so
/// they are released with it. The consumer is test-owned: it snapshots history or declares its taps at
/// setup, and its disposal (registered as a test resource) deletes whatever the adapter created for this
/// test, so a shared deployed broker cannot leak another test's messages into this one.
/// </summary>
internal sealed class ProtoMessageClientInitializer(string name) : IProtoClientInitializer<ProtoMessageClient>
{
    public string Name { get; } = name;

    public async Task<bool> TryInitializeAsync(ProtoExecutionContext context)
    {
        var broker = context.Service<IProtoMessageBroker>();
        var options = context.Service<MessagingOptions>();

        // Declarations are the run's own topology: create them before any tap binds, so a destination
        // this suite owns - it publishes to it itself - exists when the test starts. A declaration that
        // fails fails setup: the suite stated the destination exists, and its tests must not run against
        // a broker where it does not. The adapter makes repeated declarations no-ops.
        var declared = options.DeclaredDestinations
            .Where(destination => !string.IsNullOrWhiteSpace(destination))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (declared.Length > 0)
        {
            await broker.DeclareAsync(declared, CancellationToken.None);
        }

        // Test setup is not cancellable: no adapter supplies a token for it.
        var consumer = await broker.CreateConsumerAsync(CancellationToken.None);
        var prepareFailures = new Dictionary<string, Exception>(StringComparer.Ordinal);

        // Register before preparing: a failed PrepareAsync must not leak the consumer's channel or queues.
        context.RegisterResource(
            $"messaging:consumer:{Name}",
            "consumer",
            $"Messaging consumer '{Name}'",
            _ => consumer.DisposeAsync());
        if (options.Destinations.Count > 0)
        {
            // Bind the test's taps before it acts, so a message published after this point is not missed
            // for lack of a subscription. Delivery itself can still fail at the broker.
            // Tap callbacks and the configuration section can both name a destination, so the set is
            // filtered and deduped here, the one place every adapter reads it. The destinations are
            // prepared concurrently - each tap owns its channel, so the broker round trips overlap -
            // and every one is attempted: a destination that cannot be declared (a missing exchange)
            // must fail only the tests that await it, with the adapter's named error, instead of every
            // test in the class during setup. The client rethrows the recorded failure from AwaitAsync
            // for that destination.
            var destinations = options.Destinations
                .Where(destination => !string.IsNullOrWhiteSpace(destination))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var prepared = await Task.WhenAll(destinations.Select(destination => PrepareAsync(consumer, destination)));
            foreach (var (destination, failure) in prepared)
            {
                if (failure is not null)
                {
                    prepareFailures[destination] = failure;
                }
            }
        }

        context.RegisterClient(new ProtoMessageClient(context, broker, consumer, options, prepareFailures), Name);
        return true;
    }

    /// <summary>
    /// Prepares one destination and returns its failure instead of throwing, so the caller can record
    /// each destination's outcome while the others keep preparing.
    /// </summary>
    private static async Task<(string Destination, Exception? Failure)> PrepareAsync(
        IProtoMessageConsumer consumer,
        string destination)
    {
        try
        {
            await consumer.PrepareAsync([destination], CancellationToken.None);
            return (destination, null);
        }
        catch (Exception exception)
        {
            return (destination, exception);
        }
    }
}
