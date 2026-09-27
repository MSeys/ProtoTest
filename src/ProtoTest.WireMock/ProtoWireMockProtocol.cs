namespace ProtoTest.WireMock;

using ProtoTest.Core;
using ProtoTest.Rest;

/// <summary>
/// The WireMock protocol's identity: the key options and clients are registered under, the trace
/// source its operations declare, and the observation kinds its fakes record. Matched requests reuse
/// the REST response kind and payload, so the trace and any REST consumer read them as HTTP responses;
/// stub registrations and unmatched requests use the package's own kinds.
/// </summary>
public static class ProtoWireMockProtocol
{
    /// <summary>Stable protocol key WireMock registrations are tracked under.</summary>
    public const string Key = "WireMock";

    /// <summary>The capability and coverage name for the fake HTTP service.</summary>
    public const string Name = "WireMock";

    /// <summary>The trace source WireMock operations and events declare.</summary>
    public const string TraceSource = "ProtoTest.WireMock";

    /// <summary>
    /// The observation kind a matched request records: the REST response kind, with a
    /// <c>RestResponseData</c> payload, so matched fakes read as HTTP responses everywhere.
    /// </summary>
    public const string ResponseObservationKind = ProtoRestBuilder.ResponseObservationKind;

    /// <summary>
    /// The observation kind an unmatched request records: the REST failure kind, with a
    /// <c>RestFailureData</c> payload, so an unmatched call never counts as covered.
    /// </summary>
    public const string FailureObservationKind = ProtoRestBuilder.FailureObservationKind;

    /// <summary>
    /// The observation kind a stub registration records. The coverage collector turns it into an
    /// uncovered item until a matched request for the stub arrives.
    /// </summary>
    public const string StubObservationKind = "wiremock.stub";

    /// <summary>The coverage category the fake's collector reports under.</summary>
    public const string CoverageCategory = "WireMock";

    /// <summary>The operation a fake's server start is traced under.</summary>
    public const string StartOperation = "wiremock.server.start";

    /// <summary>The operation a fake's server stop is traced under.</summary>
    public const string StopOperation = "wiremock.server.stop";

    /// <summary>The protocol's identity: names, trace source, observation kind and coverage category.</summary>
    public static readonly ProtoProtocol Protocol = new(
        Key, Name, TraceSource, ResponseObservationKind, CoverageCategory);

    /// <summary>The observation target for one fake: prefixed so a fake never shares a target with a REST client of the same name.</summary>
    public static string TargetName(string fakeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fakeName);
        return $"{Name}:{fakeName}";
    }

    /// <summary>The observation identifier for one stub or request: the method and the path template, like the REST route identifier.</summary>
    public static string Identifier(string method, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return $"{method.ToUpperInvariant()} {path}";
    }
}
