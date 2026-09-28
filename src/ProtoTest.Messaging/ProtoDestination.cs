namespace ProtoTest.Messaging;

/// <summary>
/// The messaging destination addressing forms. An exchange destination is the bare name the surface
/// has always spoken: publishing sends to that exchange and awaiting binds the test's tap to it. A
/// queue destination is <c>queue:{name}</c>: it names the queue itself, and an adapter whose broker
/// owns queues consumes the queue directly instead of binding a tap - the form a dead-letter queue is
/// read with.
/// </summary>
/// <remarks>
/// A queue destination is consumed, never declared: the queue exists through whoever owns it, the
/// application's topology or the dead-letter bindings that feed it. Awaiting one reads the queue
/// itself, so a queue with a live consumer, or two tests awaiting one queue, share its deliveries;
/// prefer the exchange that feeds it when the suite runs in parallel. An adapter whose broker has no
/// queue model refuses a queue destination with <see cref="QueueUnsupported"/> instead of dropping it,
/// so the await names the transport and the fix.
/// </remarks>
public static class ProtoDestination
{
    /// <summary>The prefix that marks a destination as a queue: <c>queue:</c>.</summary>
    public const string QueueScheme = "queue:";

    /// <summary>The destination that names the queue <paramref name="name"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or whitespace.</exception>
    public static string Queue(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return QueueScheme + name;
    }

    /// <summary>Whether <paramref name="destination"/> addresses a queue.</summary>
    public static bool IsQueue(string? destination)
        => destination is not null && destination.StartsWith(QueueScheme, StringComparison.Ordinal);

    /// <summary>
    /// The queue name a queue destination carries. A destination without the queue form, or one whose
    /// name is empty, fails naming the form instead of being read as an exchange.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is not a queue destination.</exception>
    public static string QueueName(string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        if (!IsQueue(destination) || destination.Length == QueueScheme.Length)
        {
            throw new ArgumentException(
                $"'{destination}' is not a queue destination; a queue destination is " +
                $"'{QueueScheme}{{name}}'. Build one with ProtoDestination.Queue(name).",
                nameof(destination));
        }

        return destination[QueueScheme.Length..];
    }

    /// <summary>
    /// The error an adapter whose broker has no queue model throws for a queue destination: it names
    /// the destination, the adapter and the remedy, so the await fails with the caller's next step
    /// instead of timing out or silently dropping the destination.
    /// </summary>
    public static InvalidOperationException QueueUnsupported(string destination, string brokerName, string remedy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentException.ThrowIfNullOrWhiteSpace(brokerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(remedy);
        return new InvalidOperationException(
            $"Cannot await '{destination}': the {brokerName} broker has no queues. {remedy}");
    }
}
