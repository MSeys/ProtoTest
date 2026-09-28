namespace ProtoTest.Messaging.Internal;

/// <summary>
/// The routing-key half of an await: one ordinal equality over the routing key the transport recorded,
/// composed with the caller's predicate so both must hold. The composition keeps routing-key matching a
/// predicate concern for a consumer that already has the delivery, and a message that never matches
/// keeps the delivery for a later await exactly like any other predicate miss.
/// </summary>
internal static class ProtoMessageRoutingKeys
{
    /// <summary>
    /// Composes <paramref name="predicate"/> with a routing-key equality; a null routing key returns the
    /// predicate unchanged, so the plain await path costs nothing.
    /// </summary>
    public static Func<ProtoMessage, bool> Filter(Func<ProtoMessage, bool> predicate, string? routingKey)
        => routingKey is null
            ? predicate
            : message => string.Equals(message.RoutingKey, routingKey, StringComparison.Ordinal) && predicate(message);
}
