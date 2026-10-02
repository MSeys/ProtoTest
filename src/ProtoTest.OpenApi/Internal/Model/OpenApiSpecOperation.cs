namespace ProtoTest.OpenApi.Internal.Model;

/// <summary>One operation and the responses it documents.</summary>
internal sealed class OpenApiSpecOperation(IReadOnlyDictionary<string, OpenApiSpecResponse> responses)
{
    /// <summary>The responses keyed as written: a status code, a range such as <c>2XX</c>, or <c>default</c>.</summary>
    public IReadOnlyDictionary<string, OpenApiSpecResponse> Responses { get; } = responses;
}
