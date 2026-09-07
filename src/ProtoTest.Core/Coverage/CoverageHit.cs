namespace ProtoTest.Core;

/// <summary>
/// Represents an incoming coverage hit containing protocol-agnostic payload data.
/// </summary>
public record CoverageHit(
    string TargetName,
    string Identifier,
    object? Data = null,
    IReadOnlyDictionary<string, object>? Metadata = null
);