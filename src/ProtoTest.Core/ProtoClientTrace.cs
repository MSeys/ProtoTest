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
    /// <c>Operation(...)</c> in place of a bare <c>For(...)</c>. <paramref name="entityName"/> is the
    /// registration key when the logical name differs from it, so the operation links to the entity the
    /// client's configuration was recorded on.
    /// </summary>
    public static ProtoTraceScope ForClient(
        this ProtoTraceScope scope,
        Type clientType,
        string name,
        string? entityName = null)
        => scope
            .For(ProtoTraceEntityKinds.Client, Id(clientType, entityName ?? name))
            .With("client.name", name);
}
