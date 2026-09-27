namespace ProtoTest.Messaging.Internal;

/// <summary>
/// One delivery a consumer can await over, with the position it occupies in its source's arrival
/// order. Positions must be unique and increasing; a consumer only ever matches entries at or after
/// its own position.
/// </summary>
internal readonly record struct ProtoMessageAwaitEntry(long Position, ProtoMessage Message);
