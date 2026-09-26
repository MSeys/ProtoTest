namespace ProtoTest.Core.Internal;

/// <summary>
/// The one owner of the flow-scoped ambient state: the active test (its host and context) and the
/// request clock a hosting integration pushed for this flow. <see cref="ProtoTestLifecycle"/> and
/// <see cref="ProtoRequestClock"/> write through it, and every reader - <c>Proto.Context</c>,
/// <c>ProtoHost.CurrentHost</c>, <see cref="ProtoTestTimeProvider"/>, the host registry - resolves
/// through it, so the precedence between a pushed request clock and a test clock exists exactly once.
/// </summary>
internal static class ProtoAmbient
{
    private static readonly AsyncLocal<ProtoAmbientState?> Flow = new();

    /// <summary>The active test of this flow, or null when no test is running on it.</summary>
    public static ProtoTestLifecycleState? Test => Flow.Value?.Test;

    /// <summary>The host owning the active test of this flow, or null.</summary>
    public static ProtoHost? Host => Flow.Value?.Test?.Host;

    /// <summary>The clock a hosting integration pushed for this flow, or null.</summary>
    public static ProtoClock? RequestClock => Flow.Value?.RequestClock;

    /// <summary>
    /// The clock in effect on this flow: the pushed request clock wins over the active test's clock; a
    /// flow with neither has no ambient clock and the caller falls back to the run clock.
    /// </summary>
    public static ProtoClock? Clock => Flow.Value?.RequestClock ?? Flow.Value?.Test?.Context?.Clock;

    /// <summary>Makes <paramref name="state"/> the active test of this flow, keeping a pushed clock.</summary>
    public static void SetTest(ProtoTestLifecycleState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        // Copy-on-write: a background flow that captured the previous state object keeps seeing it (with
        // its context cleared on completion), instead of observing the next test started on this flow.
        var previous = Flow.Value;
        Flow.Value = new ProtoAmbientState { Test = state, RequestClock = previous?.RequestClock };
    }

    /// <summary>Removes <paramref name="state"/> from this flow when it is still the active test.</summary>
    public static void ClearTest(ProtoTestLifecycleState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (Flow.Value is { } current && ReferenceEquals(current.Test, state))
        {
            Flow.Value = new ProtoAmbientState { RequestClock = current.RequestClock };
        }
    }

    /// <summary>
    /// Makes <paramref name="clock"/> the clock of the current flow until the returned scope is disposed,
    /// restoring whatever clock was in effect before. A push does not revoke the clock from flows that
    /// captured it, so a fire-and-forget task started inside the scope keeps it after the scope ends.
    /// </summary>
    public static IDisposable PushClock(ProtoClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var previous = Flow.Value;
        Flow.Value = new ProtoAmbientState { Test = previous?.Test, RequestClock = clock };
        return new ClockScope(previous?.RequestClock);
    }

    private sealed class ClockScope(ProtoClock? previous) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                var current = Flow.Value;
                Flow.Value = new ProtoAmbientState { Test = current?.Test, RequestClock = previous };
            }
        }
    }

    private sealed class ProtoAmbientState
    {
        public ProtoTestLifecycleState? Test { get; init; }

        public ProtoClock? RequestClock { get; init; }
    }
}
