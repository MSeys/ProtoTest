namespace ProtoTest.Core;

/// <summary>
/// Represents a normalized coverage item for reporting across different test domains.
/// </summary>
public record CoverageItem(
    string TargetName,
    string Category,
    string Identifier,
    bool IsVisited,
    int HitCount = 0,
    IReadOnlyList<CoverageItem>? Children = null,
    IReadOnlyDictionary<string, object>? Metadata = null
);