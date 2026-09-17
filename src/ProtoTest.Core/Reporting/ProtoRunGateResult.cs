namespace ProtoTest.Core;

/// <summary>How a Run gate evaluated.</summary>
public enum ProtoRunGateOutcome
{
    Passed,
    Warning,
    Failed,
    Skipped
}

/// <summary>The verdict of one Run gate.</summary>
public sealed record ProtoRunGateResult(
    ProtoRunGateOutcome Outcome,
    string? Message = null,
    IReadOnlyList<string>? Details = null)
{
    public static ProtoRunGateResult Passed(string? message = null)
        => new(ProtoRunGateOutcome.Passed, message);

    /// <summary>A gate that reports a problem without failing the run.</summary>
    public static ProtoRunGateResult Warning(string message, IReadOnlyList<string>? details = null)
        => new(ProtoRunGateOutcome.Warning, message, details);

    public static ProtoRunGateResult Failed(string message, IReadOnlyList<string>? details = null)
        => new(ProtoRunGateOutcome.Failed, message, details);

    public static ProtoRunGateResult Skipped(string? message = null)
        => new(ProtoRunGateOutcome.Skipped, message);
}
