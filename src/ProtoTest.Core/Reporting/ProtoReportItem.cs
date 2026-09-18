namespace ProtoTest.Core;

/// <summary>
/// A normalized item that can be rendered by any report sink. <see cref="Kind"/> is an open string so
/// integrations can define their own; <see cref="ProtoReportItemKinds"/> names the values Core itself uses.
/// </summary>
public sealed record ProtoReportItem(
    string TargetName,
    string Category,
    string Identifier,
    string Kind = ProtoReportItemKinds.Observation,
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

/// <summary>
/// The report item kinds Core produces. A kind is an open string, so an integration can add its own;
/// sinks group unknown kinds under their own section instead of dropping them.
/// </summary>
public static class ProtoReportItemKinds
{
    public const string Observation = "observation";
    public const string Coverage = "coverage";
    public const string Finding = "finding";
    public const string Gate = "gate";
    public const string Metric = "metric";
    public const string Resource = "resource";
}

public enum ProtoReportStatus
{
    Neutral,
    Info,
    Success,
    Warning,
    Error
}
