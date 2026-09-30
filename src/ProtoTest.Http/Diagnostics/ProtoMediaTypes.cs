namespace ProtoTest.Http;

/// <summary>
/// The media type classification the HTTP integrations share. Bodies are only decoded as text for
/// text media types; anything else stays bytes so tracing never carries lossy decoded text.
/// </summary>
internal static class ProtoMediaTypes
{
    internal static bool IsTextMediaType(string? mediaType)
    {
        if (mediaType is null) return false;
        var normalized = mediaType.ToLowerInvariant();
        return normalized.StartsWith("text/", StringComparison.Ordinal)
            || normalized is "application/json" or "application/xml" or "application/yaml"
                or "application/x-yaml" or "application/javascript"
                or "application/x-www-form-urlencoded" or "application/graphql"
            || normalized.EndsWith("+json", StringComparison.Ordinal)
            || normalized.EndsWith("+xml", StringComparison.Ordinal)
            || normalized.EndsWith("+yaml", StringComparison.Ordinal);
    }
}
