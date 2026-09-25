namespace ProtoTest.Core;

/// <summary>
/// How long the host waits for a readiness probe and how often it asks. One instance governs every
/// probe of the host and every container the run starts; a probe registered with its own timeout uses
/// that instead. The section <c>ProtoTest:Readiness</c> binds over code values when the host is built.
/// </summary>
public sealed class ProtoReadinessOptions : IProtoConfigurableOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string ConfigurationSectionName = "ProtoTest:Readiness";

    /// <inheritdoc />
    string IProtoConfigurableOptions.ConfigurationSectionName => ConfigurationSectionName;

    /// <summary>Gets or sets how long a single probe may take. Defaults to 30 seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets the pause between checks. Defaults to 100 milliseconds.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <inheritdoc />
    public void Validate()
    {
        if (Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(Timeout), Timeout, "The readiness timeout must be positive.");
        }

        if (Interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(Interval), Interval, "The readiness interval must be positive.");
        }
    }
}
