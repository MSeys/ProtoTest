namespace ProtoTest.Core;

using System.Collections.Concurrent;

/// <summary>
/// The clocks of the tests one host is currently running, keyed by test id. One registry belongs to
/// one host, so two hosts that share a test id each resolve their own clock, and disposing the host
/// clears it. An in-process hosting integration links a request the test caused back to the test's
/// clock through the owning host's registry: the registry is a host service, so resolving it from the
/// test's provider yields exactly the host that started the application, and <see cref="Find"/> is
/// the public lookup an in-process transport or middleware uses.
/// </summary>
public sealed class ProtoClockRegistry
{
    private readonly ConcurrentDictionary<string, ProtoClock> _clocks = new(StringComparer.Ordinal);

    internal ProtoClockRegistry()
    {
    }

    /// <summary>Registers the clock of a test that is starting.</summary>
    internal void Add(string testId, ProtoClock clock) => _clocks[testId] = clock;

    /// <summary>Removes the clock of a test that ended; removing an absent test id is a no-op.</summary>
    internal void Remove(string testId) => _clocks.TryRemove(testId, out _);

    /// <summary>Finds the clock of the test with the given id, or <see langword="null"/> when it is not running.</summary>
    public ProtoClock? Find(string testId) => _clocks.TryGetValue(testId, out var clock) ? clock : null;

    /// <summary>Drops every clock, for host disposal.</summary>
    internal void Clear() => _clocks.Clear();
}
