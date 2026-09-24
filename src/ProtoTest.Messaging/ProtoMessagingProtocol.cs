namespace ProtoTest.Messaging;

using ProtoTest.Core;

/// <summary>
/// The messaging protocol's identity: the trace source its operations declare and the observation kind
/// a received message records. The operation names deliberately use the action verbs
/// (<c>messaging.publish</c>, <c>messaging.await</c>) while the observation uses the event
/// (<c>messaging.receive</c>): the trace shows what the test did, the observation what it saw.
/// </summary>
internal static class ProtoMessagingProtocol
{
    public static readonly ProtoProtocol Protocol = new(
        Key: "Messaging",
        Name: "Messaging",
        TraceSource: "ProtoTest.Messaging",
        ResponseObservationKind: "messaging.receive",
        CoverageCategory: "Messaging");

    /// <summary>The operation a published message is traced under.</summary>
    public const string Publish = "messaging.publish";

    /// <summary>The operation an awaited message is traced under.</summary>
    public const string Await = "messaging.await";
}
