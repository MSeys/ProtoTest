namespace ProtoTest.Core;

/// <summary>
/// A normalized item that can be rendered by any report sink.
/// </summary>
public sealed record ProtoReportItem(
    string TargetName,
    string Category,
    string Identifier,
    ProtoReportItemKind Kind = ProtoReportItemKind.Observation,
    ProtoReportStatus Status = ProtoReportStatus.Neutral,
    int Count = 0,
    bool? IsCovered = null,
    double? Value = null,
    string? Unit = null,
    string? Message = null,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyList<ProtoReportItem>? Children = null,
    IReadOnlyDictionary<string, object>? Metadata = null,
    string? DisplayName = null,
    string? DisplayGroup = null);

public enum ProtoReportItemKind
{
    Observation,
    Coverage,
    Finding,
    Gate,
    Metric
}

public enum ProtoReportStatus
{
    Neutral,
    Info,
    Success,
    Warning,
    Error
}
