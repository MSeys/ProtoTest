namespace ProtoTest.Json;

using System.Text.Json;

/// <summary>
/// Shared serializer defaults, so every protocol reads and writes JSON the same way. Options freeze on
/// first use; nothing in the framework mutates these instances.
/// </summary>
public static class ProtoJsonDefaults
{
    /// <summary>Reads a response: property names match case-insensitively, no naming policy is applied.</summary>
    public static JsonSerializerOptions Reader { get; } = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Reads and writes request payloads: web defaults, so camelCase names and case-insensitive reads.</summary>
    public static JsonSerializerOptions Web { get; } = new(JsonSerializerDefaults.Web);
}
