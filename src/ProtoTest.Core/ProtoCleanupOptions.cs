namespace ProtoTest.Core;

/// <summary>
/// What a failing cleanup does to the test the runner reports. Cleanup is the work after the body:
/// after-test hooks and attributes, publishing attachments, releasing resources and disposing clients.
/// </summary>
public enum ProtoCleanupFailureMode
{
    /// <summary>
    /// The test fails. A body that passed is reported as failed, with a message that says so and names
    /// the cleanup failure. A body that failed keeps that failure; the cleanup failures are attached to
    /// it. This is the default.
    /// </summary>
    Fail,

    /// <summary>
    /// The cleanup failure is recorded as a finding and does not change the result the test reported.
    /// </summary>
    Report
}

/// <summary>
/// How the run treats a cleanup failure. The section <c>ProtoTest</c> binds over code values when the
/// host is built, so <c>ProtoTest:CleanupFailures</c> selects <see cref="ProtoCleanupFailureMode.Fail"/>
/// or <see cref="ProtoCleanupFailureMode.Report"/>.
/// </summary>
public sealed class ProtoCleanupOptions : IProtoConfigurableOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string ConfigurationSectionName = "ProtoTest";

    /// <inheritdoc />
    string IProtoConfigurableOptions.ConfigurationSectionName => ConfigurationSectionName;

    /// <summary>
    /// How a failing cleanup is reported. Defaults to <see cref="ProtoCleanupFailureMode.Fail"/>.
    /// </summary>
    public ProtoCleanupFailureMode CleanupFailures { get; set; } = ProtoCleanupFailureMode.Fail;

    /// <inheritdoc />
    public void Validate()
    {
        if (!Enum.IsDefined(CleanupFailures))
        {
            throw new ArgumentOutOfRangeException(
                nameof(CleanupFailures),
                CleanupFailures,
                "CleanupFailures must be Fail or Report.");
        }
    }
}
