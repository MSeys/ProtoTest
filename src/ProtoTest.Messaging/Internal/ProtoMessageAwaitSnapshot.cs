namespace ProtoTest.Messaging.Internal;

/// <summary>
/// One scan of a consumer's source: the candidate deliveries at or after the awaited position, and the
/// wake-up the scan arms for the deliveries that arrive after it. A source whose deliveries are already
/// held - a log the consumer reads itself - leaves <see cref="Changed"/> null, because its next wait
/// watches that storage directly.
/// </summary>
internal readonly record struct ProtoMessageAwaitSnapshot(
    IReadOnlyList<ProtoMessageAwaitEntry> Candidates,
    Task? Changed);
