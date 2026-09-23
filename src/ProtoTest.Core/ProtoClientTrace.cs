namespace ProtoTest.Core;

/// <summary>
/// The client entity a traced operation belongs to. Every protocol records its client under the same
/// id and name, so the viewer places a REST, GraphQL, gRPC or web operation on that client's timeline
/// together.
/// </summary>
public static class ProtoClientTrace
{
    /// <summary>The entity id a client is recorded under: its concrete type and the name it was resolved by.</summary>
    public static string Id(Type clientType, string name)
    {
        ArgumentNullException.ThrowIfNull(clientType);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return $"client:{clientType.FullName}:{name}";
    }

    /// <summary>
    /// Ties the operation to the client entity and records the client's name on it. Chain after
    /// <c>Operation(...)</c> in place of a bare <c>For(...)</c>.
    /// </summary>
    public static ProtoTraceScope ForClient(this ProtoTraceScope scope, Type clientType, string name)
        => scope
            .For(ProtoTraceEntityKinds.Client, Id(clientType, name))
            .With("client.name", name);
}
