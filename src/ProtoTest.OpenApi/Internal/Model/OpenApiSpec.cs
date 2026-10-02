namespace ProtoTest.OpenApi.Internal.Model;

/// <summary>
/// The parts of an OpenAPI document coverage reads: its paths, their operations, their responses and the
/// response schemas. ProtoTest reads documents itself, so a suite does not impose an OpenAPI library
/// version on the application it hosts.
/// </summary>
internal sealed class OpenApiSpec(IReadOnlyDictionary<string, OpenApiSpecPath> paths)
{
    /// <summary>The document's paths in document order, keyed by the path template.</summary>
    public IReadOnlyDictionary<string, OpenApiSpecPath> Paths { get; } = paths;
}
