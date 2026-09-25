namespace ProtoTest.Core;

/// <summary>
/// What makes one protocol itself: the key its options are registered under, the name capabilities
/// and coverage use, the trace source its operations declare, and the observation kind coverage
/// collects. One descriptor per integration, so the builder that produces an observation and the
/// collector that consumes it cannot disagree about its kind. <see cref="CoverageCategory"/> is
/// <see langword="null"/> for a protocol that ships no collector (Messaging): it owns no coverage
/// vocabulary, so it declares none.
/// </summary>
public sealed record ProtoProtocol(
    string Key,
    string Name,
    string TraceSource,
    string ResponseObservationKind,
    string? CoverageCategory = null)
{
    /// <summary>The capability this protocol declares when it is registered.</summary>
    public ProtoCapabilityDescriptor Capability { get; } =
        new(Name, ProtoCapabilityKinds.Protocol, TraceSource);
}
