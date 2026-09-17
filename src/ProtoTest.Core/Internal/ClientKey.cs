namespace ProtoTest.Core.Internal;

/// <summary>Identifies a named client by its registration type and name (name is case-insensitive).</summary>
internal readonly record struct ClientKey(Type ClientType, string Name);

internal sealed class ClientKeyComparer : IEqualityComparer<ClientKey>
{
    public static ClientKeyComparer Instance { get; } = new();

    public bool Equals(ClientKey x, ClientKey y)
        => x.ClientType == y.ClientType
           && StringComparer.OrdinalIgnoreCase.Equals(x.Name, y.Name);

    public int GetHashCode(ClientKey obj)
        => HashCode.Combine(obj.ClientType, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name));
}
