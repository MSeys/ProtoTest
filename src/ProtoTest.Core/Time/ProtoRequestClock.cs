namespace ProtoTest.Core;

/// <summary>
/// The clock in effect for the current request or flow. A hosting integration that runs an application
/// in-process pushes the test's clock while it handles a request the test caused, so application code
/// that resolves <see cref="TimeProvider"/> sees the test's time; background flows see the run's.
/// </summary>
/// <remarks>
/// A push restores whatever clock was in effect before it when its scope is disposed; it does not
/// revoke the pushed clock from flows that captured it. A fire-and-forget task started inside a
/// request keeps the finished test's clock after the request and the test end, so long-lived
/// background work must read the run clock instead of the ambient one. The lookup that links a
/// request to a test is scoped to the host that owns the test (<see cref="ProtoHost.FindClock"/>).
/// </remarks>
public static class ProtoRequestClock
{
    private static readonly AsyncLocal<ProtoClock?> Ambient = new();

    /// <summary>Gets the pushed clock, or <see langword="null"/> when the flow has none.</summary>
    public static ProtoClock? Current => Ambient.Value;

    /// <summary>
    /// Makes <paramref name="clock"/> the clock of the current flow until the returned scope is
    /// disposed, restoring whatever clock was in effect before. Hosting integrations call this around
    /// the work a test caused.
    /// </summary>
    public static IDisposable Push(ProtoClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var previous = Ambient.Value;
        Ambient.Value = clock;
        return new Scope(previous);
    }

    private sealed class Scope(ProtoClock? previous) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Ambient.Value = previous;
            }
        }
    }
}
