namespace ProtoTest.Json;

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>One wire-shaped member of a CLR object: the name JSON uses, the declared type and the value.</summary>
public readonly record struct ProtoJsonProperty(string Name, Type Type, object? Value);

/// <summary>
/// Reads a CLR object as the members JSON sees: public, non-indexed properties, honoring
/// <see cref="JsonPropertyNameAttribute"/> and <see cref="JsonIgnoreAttribute"/>, with the caller's
/// naming policy as the fallback. One implementation, so request bodies, query strings, GraphQL
/// documents and expected shapes cannot disagree about a wire name.
/// </summary>
public static class ProtoJsonPropertyProjection
{
    /// <summary>Projects a value's members. A null value still yields the type's shape with null values.</summary>
    public static IReadOnlyList<ProtoJsonProperty> Read(
        Type type,
        object? value,
        JsonNamingPolicy? namingPolicy = null,
        bool honorJsonIgnore = true)
    {
        ArgumentNullException.ThrowIfNull(type);
        var properties = new List<ProtoJsonProperty>();
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            if (honorJsonIgnore && property.GetCustomAttribute<JsonIgnoreAttribute>() is not null)
            {
                continue;
            }

            var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                ?? namingPolicy?.ConvertName(property.Name)
                ?? property.Name;
            properties.Add(new ProtoJsonProperty(name, property.PropertyType, value is null ? null : property.GetValue(value)));
        }

        return properties;
    }

    /// <summary>Projects a non-null value's members.</summary>
    public static IReadOnlyList<ProtoJsonProperty> Read(
        object value,
        JsonNamingPolicy? namingPolicy = null,
        bool honorJsonIgnore = true)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Read(value.GetType(), value, namingPolicy, honorJsonIgnore);
    }
}
