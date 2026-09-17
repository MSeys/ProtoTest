namespace ProtoTest.Core;

/// <summary>Describes what happened to an owned resource.</summary>
public enum ProtoResourceState
{
    Registered,
    Released,
    ReleaseFailed
}

/// <summary>An immutable view of one resource owned by a test.</summary>
public sealed record ProtoResourceSnapshot(
    string Id,
    string Kind,
    string Description,
    ProtoResourceState State,
    TimeSpan? ReleaseDuration,
    string? Error);
