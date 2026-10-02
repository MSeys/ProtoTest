namespace ProtoTest.OpenApi.Internal.Model;

/// <summary>One documented response and the schemas its body can have, one per media type.</summary>
internal sealed class OpenApiSpecResponse(IReadOnlyList<OpenApiSpecSchema> bodySchemas)
{
    public IReadOnlyList<OpenApiSpecSchema> BodySchemas { get; } = bodySchemas;
}
