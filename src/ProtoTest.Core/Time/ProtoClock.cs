namespace ProtoTest.Core;

/// <summary>
/// The clock a suite controls: a <see cref="TimeProvider"/> whose UTC time only moves when the test
/// moves it. The run owns one instance (seeding each test's clock), and every test gets its own, so
/// advancing time in one test never leaks into another. Only <see cref="GetUtcNow"/> is virtual:
/// timers created from this provider still run on real time.
/// </summary>
public sealed class ProtoClock : TimeProvider
{
    private readonly ProtoLock _gate = new();
    private DateTimeOffset _utcNow;

    /// <summary>Creates a clock at the current real time, for a run that does not care where it starts.</summary>
    public ProtoClock()
        : this(DateTimeOffset.UtcNow)
    {
    }

    /// <summary>Creates a clock at a fixed instant, so every test starts from the same moment.</summary>
    public ProtoClock(DateTimeOffset startUtc)
    {
        _utcNow = startUtc;
    }

    /// <inheritdoc />
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Local;

    /// <summary>Raised after the clock moved; the owning host records advances in the trace.</summary>
    internal event Action<ProtoClockChange>? Advanced;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _utcNow;
        }
    }

    /// <summary>Moves the clock forward, which is what a time-dependent test does instead of sleeping.</summary>
    public void Advance(TimeSpan delta)
    {
        if (delta < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delta), delta, "A clock can only advance; use SetUtcNow to move it backwards.");
        }

        Move(GetUtcNow().Add(delta));
    }

    /// <summary>Sets the clock to an exact instant, for tests that jump rather than elapse.</summary>
    public void SetUtcNow(DateTimeOffset utcNow) => Move(utcNow);

    private void Move(DateTimeOffset utcNow)
    {
        Action<ProtoClockChange>? advanced;
        ProtoClockChange change;
        lock (_gate)
        {
            change = new ProtoClockChange(_utcNow, utcNow);
            _utcNow = utcNow;
            advanced = Advanced;
        }

        advanced?.Invoke(change);
    }
}

/// <summary>One move of a clock, for the trace and for seeding.</summary>
internal readonly record struct ProtoClockChange(DateTimeOffset PreviousUtc, DateTimeOffset CurrentUtc)
{
    public TimeSpan Delta => CurrentUtc - PreviousUtc;
}
