namespace ProtoTest.Verification;

/// <summary>
/// The severity each finding class gets. The defaults are a PR gate: a regression, a stale
/// specification and a failed gate fail the verdict, a new uncovered unit warns. An agent-PR gate
/// overrides them from its own inputs.
/// </summary>
public sealed record ProtoVerificationOptions
{
    /// <summary>Gets the severity of a regressed unit; defaults to <see cref="ProtoVerificationSeverities.Fail"/>.</summary>
    public string RegressedSeverity { get; init; } = ProtoVerificationSeverities.Fail;

    /// <summary>Gets the severity of a new uncovered unit; defaults to <see cref="ProtoVerificationSeverities.Warn"/>.</summary>
    public string AddedUncoveredSeverity { get; init; } = ProtoVerificationSeverities.Warn;

    /// <summary>Gets the severity of a specification mismatch; defaults to <see cref="ProtoVerificationSeverities.Fail"/>.</summary>
    public string StaleSpecSeverity { get; init; } = ProtoVerificationSeverities.Fail;

    /// <summary>Gets the severity of a failed run gate; defaults to <see cref="ProtoVerificationSeverities.Fail"/>.</summary>
    public string GateFailureSeverity { get; init; } = ProtoVerificationSeverities.Fail;

    /// <summary>The default severities, the shape a PR gate uses when it passes none.</summary>
    public static ProtoVerificationOptions Default { get; } = new();
}
