namespace ProtoTest.Core;

/// <summary>
/// The clock in effect for the current request or flow. A hosting integration that runs an application
/// in-process pushes the test's clock while it handles a request the test caused, so application code
/// that resolves <see cref="TimeProvider"/> sees the test's time; background flows see the run's.
/// </summary>
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

/// <summary>Finds a running test's clock by test id, for hosting integrations linking a request back.</summary>
internal static class ProtoClockLocator
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ProtoClock> Clocks = new(StringComparer.Ordinal);

    public static void Add(string testId, ProtoClock clock) => Clocks[testId] = clock;

    public static void Remove(string testId) => Clocks.TryRemove(testId, out _);

    public static ProtoClock? Find(string testId) => Clocks.TryGetValue(testId, out var clock) ? clock : null;
}
