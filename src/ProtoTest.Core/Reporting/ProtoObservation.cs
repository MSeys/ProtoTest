namespace ProtoTest.Core;

/// <summary>
/// A protocol-agnostic fact observed during a test execution.
/// </summary>
public sealed record ProtoObservation(
    string TargetName,
    string Kind,
    string Identifier,
    object? Data = null,
    IReadOnlyDictionary<string, object>? Metadata = null);
