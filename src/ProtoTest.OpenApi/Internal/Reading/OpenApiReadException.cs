namespace ProtoTest.OpenApi.Internal.Reading;

/// <summary>Builds the one failure every unreadable document reports, with the reason it was rejected.</summary>
internal static class OpenApiReadException
{
    public static InvalidOperationException Create(string reason, Exception? innerException = null)
        => new($"Failed to parse OpenAPI specification: {reason}", innerException);
}
