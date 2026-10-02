namespace ProtoTest.OpenApi.Internal.Model;

/// <summary>
/// A schema with the structure coverage walks: its properties, its array items and its compositions. A
/// <c>$ref</c> resolves to the shared instance of its target, so a cyclic schema is a cyclic graph and the
/// walk detects the cycle by reference.
/// </summary>
internal sealed class OpenApiSpecSchema
{
    public Dictionary<string, OpenApiSpecSchema> Properties { get; } = new(StringComparer.Ordinal);

    public OpenApiSpecSchema? Items { get; set; }

    /// <summary>The <c>allOf</c>, <c>oneOf</c> and <c>anyOf</c> members, which all contribute properties.</summary>
    public List<OpenApiSpecSchema> Compositions { get; } = [];
}
