namespace ProtoTest.Grpc;

using System.Text.Json;
using global::Google.Protobuf;
using ProtoTest.Json;

/// <summary>
/// Shape assertions for gRPC replies, reusing the Json shape matcher the REST and GraphQL integrations
/// use. Protobuf maps to Json with camelCase field names, so an expected anonymous shape reads the same
/// way as elsewhere.
/// </summary>
public static class ProtoGrpcAssertions
{
    private static readonly JsonFormatter Formatter = new(
        JsonFormatter.Settings.Default.WithFormatDefaultValues(true).WithFormatEnumsAsIntegers(false));

    /// <summary>Matches one protobuf reply against an expected shape.</summary>
    public static void ShouldMatchShape<TResponse>(
        this TResponse response,
        object expectedShape,
        JsonSerializerOptions? options = null)
        where TResponse : IMessage
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(expectedShape);
        var json = Formatter.Format(response);
        _ = JsonShapeMatcher.AssertMatch(json, expectedShape, options);
    }
}
