namespace ProtoTest.Core;

/// <summary>
/// One readiness check for a piece the run starts: a database that must accept connections, a broker
/// that must answer, an application address that must respond. A check is polled until it returns
/// <see langword="true"/> or the run's readiness timeout expires; exceptions are treated as "not ready
/// yet" and remembered for the failure message, so a connection-refused during a container's boot is
/// normal.
/// </summary>
public interface IProtoReadinessProbe
{
    /// <summary>Gets a name for the trace and the timeout message.</summary>
    string Name { get; }

    /// <summary>Checks once; <see langword="true"/> means ready.</summary>
    ValueTask<bool> CheckAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// How long the host waits for a readiness probe and how often it asks. One instance governs every
/// probe of the host; a probe registered with its own timeout uses that instead.
/// </summary>
public sealed class ProtoReadinessOptions
{
    /// <summary>Gets or sets how long a single probe may take. Defaults to 30 seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets the pause between checks. Defaults to 100 milliseconds.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMilliseconds(100);

    internal void Validate()
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
