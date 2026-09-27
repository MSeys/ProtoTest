namespace ProtoTest.WireMock;

using ProtoTest.WireMock.Internal;

/// <summary>
/// Describes one fake HTTP service on the <c>AddWireMock</c> chain. A fake starts lazily on the first
/// <c>WireMock(name)</c> call of a test: per-test fakes start a fresh server per test on a dynamic
/// port, while <see cref="PerRun"/> shares one server - stubs and request log included - across the
/// run and clears them when the run releases it.
/// </summary>
public sealed class ProtoWireMockBuilder
{
    private readonly string _name;
    private bool _perRun;
    private int? _port;

    internal ProtoWireMockBuilder(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
    }

    /// <summary>The fake's name, used by <c>WireMock(name)</c>, the capability instance and the target.</summary>
    public string Name => _name;

    /// <summary>
    /// Shares one server across the run instead of starting a fresh server per test. The shared
    /// server's stubs and request log live for the whole run, so a stub one test registers keeps
    /// matching in the next and the log is shared; call <c>Reset()</c> to clear both mid-run, or keep
    /// the fake for state a suite deliberately shares. Parallel tests share the log, and observations
    /// are attributed to the test whose teardown observed them.
    /// </summary>
    public ProtoWireMockBuilder PerRun()
    {
        _perRun = true;
        return this;
    }

    /// <summary>
    /// Listens on a fixed port instead of a dynamic one, for a system under test that reads its
    /// dependency's address once at startup. Parallel tests must not share a fixed-port fake.
    /// </summary>
    public ProtoWireMockBuilder Port(int port)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        _port = port;
        return this;
    }

    internal ProtoWireMockSettings Build() => new(_perRun, _port);
}
