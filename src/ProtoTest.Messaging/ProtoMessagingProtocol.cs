namespace ProtoTest.Messaging;

using ProtoTest.Core;

/// <summary>
/// The messaging protocol's identity: the trace source its operations declare and the observation kinds
/// it records. The operation names deliberately use the action verbs (<c>messaging.publish</c>,
/// <c>messaging.await</c>) while the observations use the event (<c>messaging.receive</c>,
/// <c>messaging.published</c>, <c>messaging.failure</c>, <c>messaging.contract.shape</c>): the trace
/// shows what the test did, the observation what it saw.
/// </summary>
internal static class ProtoMessagingProtocol
{
    public static readonly ProtoProtocol Protocol = new(
        Key: "Messaging",
        Name: "Messaging",
        TraceSource: "ProtoTest.Messaging",
        ResponseObservationKind: "messaging.receive");

    /// <summary>The operation a published message is traced under.</summary>
    public const string Publish = "messaging.publish";

    /// <summary>
    /// The observation kind a successful publish records. It is deliberately event-shaped and distinct
    /// from the <see cref="Publish"/> operation verb: the trace shows what the test did, the observation
    /// what it saw, and a collector can filter the published-message event without colliding with the
    /// operation vocabulary.
    /// </summary>
    public const string PublishObservationKind = "messaging.published";

    /// <summary>The operation an awaited message is traced under.</summary>
    public const string Await = "messaging.await";

    /// <summary>
    /// The observation kind a failed publish or await records. Like the other failure kinds it is not
    /// the receive kind: a call that failed was attempted, not received.
    /// </summary>
    public const string FailureObservationKind = "messaging.failure";

    /// <summary>
    /// The observation kind a successful message shape assertion records. It is trace evidence like the
    /// publish and receive observations; the package ships no collector and aggregates no destinations.
    /// </summary>
    public const string ShapeObservationKind = "messaging.contract.shape";
}
