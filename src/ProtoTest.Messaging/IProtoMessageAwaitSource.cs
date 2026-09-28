namespace ProtoTest.Messaging;

/// <summary>
/// The ordered deliveries one consumer awaits over, plus the wait that wakes its scan. An adapter
/// implements it over its own storage: the in-memory broker and the MassTransit harness over their
/// published history, the RabbitMQ tap over its log and channel. The consumer owns a
/// <see cref="ProtoMessageAwaitQueue"/> and supplies this source; the queue enforces the serialized,
/// consumed-once semantics.
/// </summary>
public interface IProtoMessageAwaitSource
{
    /// <summary>
    /// Returns the candidate deliveries at or after <paramref name="position"/>, in arrival order. The
    /// scan arms the wait that follows it: a delivery that lands after this call completes the next
    /// <see cref="WaitAsync"/> immediately, so the scan-then-wait cannot miss it. A source whose
    /// deliveries are already held can ignore the arming and leave <c>Changed</c> null.
    /// </summary>
    ValueTask<ProtoMessageAwaitSnapshot> SnapshotAsync(
        string destination,
        long position,
        CancellationToken cancellationToken);

    /// <summary>
    /// Waits until a delivery may have arrived since the last snapshot, or the remaining time elapses,
    /// whichever comes first; a cancelled <paramref name="cancellationToken"/> throws. The queue always
    /// scans after this returns, so a delivery that landed exactly at the deadline is still seen.
    /// </summary>
    ValueTask WaitAsync(
        ProtoMessageAwaitSnapshot snapshot,
        TimeSpan remaining,
        CancellationToken cancellationToken);
}
