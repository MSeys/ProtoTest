namespace ProtoTest.OpenApi.Internal.Model;

/// <summary>One path of the document and the operations it declares.</summary>
internal sealed class OpenApiSpecPath(IReadOnlyDictionary<string, OpenApiSpecOperation> operations)
{
    /// <summary>The operations keyed by their upper-case HTTP method, in document order.</summary>
    public IReadOnlyDictionary<string, OpenApiSpecOperation> Operations { get; } = operations;
}
