namespace ProtoTest.Core;

/// <summary>Where a value version came from, so a reader can tell "not visible" from "did not happen".</summary>
public enum ProtoTraceValueSource
{
    /// <summary>Created or changed by test-side code: ProtoTest.Data, a composed domain, the test itself.</summary>
    TestSide,

    /// <summary>Seen in what the system returned; the identity was observed, not acted on.</summary>
    Observed,

    /// <summary>Reported by the application itself (future: observer, outbox or telemetry bridge).</summary>
    ApplicationSide
}

/// <summary>One state of a world item, and what produced it. Shared by values and entities.</summary>
public sealed record ProtoTraceVersion(
    string? OperationId,
    string Change,
    DateTimeOffset AtUtc,
    IReadOnlyDictionary<string, string?> State,
    ProtoTraceValueSource Source = ProtoTraceValueSource.TestSide,
    bool Inferred = false);

/// <summary>
/// A domain object the run touched - a tenant, project, invoice, message. Values are the data side of the
/// trace: identity plus versions, so a reader can follow how a value changed, not just that it appeared.
/// </summary>
public sealed record ProtoTraceValue(
    string Id,
    string Kind,
    string Name,
    string? Scope,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc,
    IReadOnlyList<ProtoTraceVersion> Versions);

/// <summary>
/// What a run could see, recorded so a gap is explained rather than implied: visibility is integration
/// depth, not hosting mode.
/// </summary>
public sealed record ProtoTraceVisibility(
    string Hosting,
    IReadOnlyList<string> Backends,
    IReadOnlyList<string> Sources,
    bool ApplicationInstrumented = false)
{
    public static ProtoTraceVisibility Unknown { get; } = new("unknown", [], []);
}
