namespace ProtoTest.Web.Internal;

/// <summary>
/// Page identity for web coverage: the absolute path of an HTTP(S) address, without query or fragment,
/// normalized to a leading slash and no trailing slash (except the root). One page, one identity, so a
/// visit, a verification and an inventory entry land on the same coverage item. Query and fragment are
/// stripped before anything else, so a relative path carrying an absolute URL in its query is still a
/// page. Percent-encoding is decoded per segment on both the visit and the inventory side, so
/// <c>/a%20b</c> and <c>/a b</c> are one identity; a segment that decodes to a slash keeps it encoded
/// (<c>%2F</c>) so one segment never becomes two.
/// </summary>
internal static class WebPagePath
{
    /// <summary>The coverage path of a full address, or <see langword="null"/> when it is not a page.</summary>
    public static string? FromAddress(string? address)
        => Uri.TryCreate(address, UriKind.Absolute, out var uri) ? FromUri(uri) : null;

    /// <summary>The coverage path of a full address, or <see langword="null"/> when it is not a page.</summary>
    public static string? FromUri(Uri? address)
    {
        if (address is null || !address.IsAbsoluteUri) return null;
        if (!string.Equals(address.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(address.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return NormalizeSegments(address.AbsolutePath);
    }

    /// <summary>Normalizes a configured path, a discovered route or a route pattern to the page identity.</summary>
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var value = StripQueryAndFragment(path.Trim());
        if (value.Length == 0) return null;
        if (value.Contains("://", StringComparison.Ordinal)) return FromAddress(value);
        return NormalizeSegments(value);
    }

    /// <summary>
    /// Normalizes a route definition: like <see cref="Normalize"/>, but dynamic segments are mapped
    /// first, so a Vue <c>:id</c>, a React <c>*</c> and a Next/Nuxt/Remix <c>[…]</c> file segment become
    /// the same <c>{name}</c> or <c>{...}</c> pattern the inventory matches.
    /// </summary>
    public static string? NormalizeRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route)) return null;
        var value = StripQueryAndFragment(route.Trim());
        if (value.Length == 0) return null;
        var segments = value
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(MapDynamicSegment)
            .Where(segment => segment.Length > 0);
        var joined = string.Join('/', segments);
        // The root route "/" has no segments; it is a page, not an empty definition. Normalize returns
        // null for an empty value, which would silently drop it from discovery.
        return joined.Length == 0 ? Normalize("/") : Normalize(joined);
    }

    /// <summary>
    /// Maps one route-definition segment to its pattern: a star, a bare Remix splat and a bracketed
    /// catch-all become <c>{...}</c>; <c>:name</c>, <c>:name?</c>, <c>[...]</c> and <c>$name</c> become
    /// <c>{name}</c>; a Vue trailing splat (<c>:rest*</c>) and a regex catch-all
    /// (<c>:pathMatch(.*)*</c>, <c>:catchAll(.*)</c>) become <c>{...}</c>; a regex-constrained
    /// parameter (<c>:id(\d+)</c>) drops the constraint and becomes <c>{id}</c>; anything else is a
    /// literal.
    /// </summary>
    internal static string MapDynamicSegment(string segment)
    {
        if (segment == "*" || segment == "$") return "{...}";
        if (segment.StartsWith('$') && segment.Length > 1) return "{" + segment[1..] + "}";
        if (segment.StartsWith('[') && segment.EndsWith(']'))
        {
            var inner = segment.Trim('[', ']');
            return inner.StartsWith("...", StringComparison.Ordinal) ? "{...}" : "{" + inner + "}";
        }

        if (segment.StartsWith(':') && segment.Length > 1) return MapVueParameter(segment[1..]);
        return segment;
    }

    /// <summary>
    /// Maps the body of a Vue router parameter. A trailing <c>*</c> is a splat; a parenthesized regex
    /// whose pattern matches the rest of the path (<c>(.*)</c>) is a catch-all; any other regex is a
    /// constraint that does not survive into the page pattern.
    /// </summary>
    private static string MapVueParameter(string body)
    {
        var catchAll = body.EndsWith('*');
        if (catchAll) body = body[..^1];
        else if (body.EndsWith('?')) body = body[..^1];

        var open = body.IndexOf('(');
        if (open >= 0)
        {
            var close = body.LastIndexOf(')');
            var pattern = close > open ? body[(open + 1)..close] : body[(open + 1)..];
            if (pattern.Contains(".*", StringComparison.Ordinal)) catchAll = true;
            body = body[..open];
        }

        if (catchAll) return "{...}";
        return body.Length > 0 ? "{" + body + "}" : "{...}";
    }

    /// <summary>
    /// Whether a concrete <paramref name="path"/> matches an inventory <paramref name="pattern"/>:
    /// segment-wise and case-insensitive, where <c>{name}</c> matches exactly one segment and
    /// <c>{...}</c> matches the rest of the path, including none.
    /// </summary>
    public static bool Matches(string? pattern, string? path)
    {
        var normalizedPattern = Normalize(pattern);
        var normalizedPath = Normalize(path);
        if (normalizedPattern is null || normalizedPath is null) return false;
        var patternSegments = normalizedPattern.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var pathSegments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        int patternIndex = 0, pathIndex = 0;
        while (patternIndex < patternSegments.Length)
        {
            if (patternSegments[patternIndex] == "{...}")
            {
                // A rest pattern consumes the rest of the path, so a literal after it can never be
                // satisfied; only a trailing rest pattern is a match.
                return patternIndex == patternSegments.Length - 1;
            }

            if (pathIndex >= pathSegments.Length) return false;
            var segment = patternSegments[patternIndex++];
            if (!IsParameter(segment)
                && !string.Equals(segment, pathSegments[pathIndex], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            pathIndex++;
        }

        return pathIndex == pathSegments.Length;
    }

    private static string StripQueryAndFragment(string value)
    {
        var separator = value.IndexOfAny(['?', '#']);
        return separator < 0 ? value : value[..separator];
    }

    private static string NormalizeSegments(string path)
    {
        var segments = path
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(DecodeSegment);
        var normalized = "/" + string.Join('/', segments);
        return normalized.Length > 1 ? normalized.TrimEnd('/') : normalized;
    }

    /// <summary>
    /// Decodes one already-split segment. A decoded slash is kept encoded, because a slash is the
    /// segment separator: decoding it before splitting would turn one segment into two.
    /// </summary>
    private static string DecodeSegment(string segment)
    {
        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(segment);
        }
        catch (UriFormatException)
        {
            return segment;
        }

        return decoded.IndexOf('/') < 0 ? decoded : decoded.Replace("/", "%2F", StringComparison.Ordinal);
    }

    private static bool IsParameter(string segment)
        => segment.Length >= 2 && segment[0] == '{' && segment[^1] == '}';
}
