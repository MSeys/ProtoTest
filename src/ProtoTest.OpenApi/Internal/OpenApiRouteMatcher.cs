namespace ProtoTest.OpenApi.Internal;

using Microsoft.OpenApi;
using ProtoTest.Http;

/// <summary>
/// Matches a request route to the document's paths and a status code to a response key: literal
/// segments win, a constrained parameter such as <c>{id:int}</c> only matches a value its constraint
/// accepts, and the comparison ignores case. One matcher serves one document.
/// </summary>
internal sealed class OpenApiRouteMatcher(OpenApiDocument document)
{
    private readonly OpenApiDocument _document = document ?? throw new ArgumentNullException(nameof(document));

    /// <summary>Finds the contract path a request route addresses, or <see langword="null"/> when none does.</summary>
    public string? Find(string template)
    {
        var normalizedTemplate = Normalize(template);
        return _document.Paths.Keys
            .Select(path => new { Path = path, Score = MatchRoute(normalizedTemplate, Normalize(path)) })
            .Where(candidate => candidate.Score >= 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Path)
            .FirstOrDefault();
    }

    /// <summary>Finds the response key a status code maps to: the exact code, its wildcard, else "default".</summary>
    public static string? FindResponseKey(OpenApiOperation operation, int statusCode)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.Responses is not { } responses) return null;

        var exact = statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (responses.ContainsKey(exact)) return exact;

        var wildcard = $"{statusCode / 100}XX";
        var wildcardKey = responses.Keys.FirstOrDefault(
            key => string.Equals(key, wildcard, StringComparison.OrdinalIgnoreCase));
        if (wildcardKey is not null) return wildcardKey;

        return responses.Keys.FirstOrDefault(
            key => string.Equals(key, "default", StringComparison.OrdinalIgnoreCase));
    }

    private static int MatchRoute(string requestRoute, string contractRoute)
    {
        if (string.Equals(requestRoute, contractRoute, StringComparison.OrdinalIgnoreCase))
        {
            return int.MaxValue;
        }

        var requestSegments = requestRoute.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var contractSegments = contractRoute.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (requestSegments.Length != contractSegments.Length) return -1;

        var literalMatches = 0;
        for (var index = 0; index < requestSegments.Length; index++)
        {
            if (string.Equals(requestSegments[index], contractSegments[index], StringComparison.OrdinalIgnoreCase))
            {
                literalMatches++;
                continue;
            }

            if (IsRouteParameter(contractSegments[index]))
            {
                // A constrained parameter such as {id:int} only matches a value the constraint
                // accepts, so /users/abc must not cover /users/{id:int}.
                if (!IsRouteParameter(requestSegments[index])
                    && !MatchesConstraints(requestSegments[index], contractSegments[index]))
                {
                    return -1;
                }

                continue;
            }

            // The segments differ and the contract segment is not a parameter: no match.
            return -1;
        }

        return literalMatches;
    }

    private static bool IsRouteParameter(string segment)
        => segment.Length > 2 && segment[0] == '{' && segment[^1] == '}';

    /// <summary>Checks a request segment against the constraints of a contract segment like <c>{id:int}</c>.</summary>
    private static bool MatchesConstraints(string value, string contractSegment)
    {
        var inner = contractSegment[1..^1];
        var parts = inner.Split(':', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 1; index < parts.Length; index++)
        {
            var constraint = parts[index];
            var argument = string.Empty;
            var open = constraint.IndexOf('(');
            if (open >= 0 && constraint.EndsWith(')'))
            {
                argument = constraint[(open + 1)..^1];
                constraint = constraint[..open];
            }

            var matches = constraint.ToLowerInvariant() switch
            {
                "int" => int.TryParse(value, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out _),
                "long" => long.TryParse(value, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out _),
                "decimal" or "double" or "float" => decimal.TryParse(value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out _),
                "guid" => Guid.TryParse(value, out _),
                "bool" => bool.TryParse(value, out _),
                "minlength" => int.TryParse(argument, out var minimum) && value.Length >= minimum,
                "maxlength" => int.TryParse(argument, out var maximum) && value.Length <= maximum,
                // An unknown constraint is not evidence the route does not match.
                _ => true
            };
            if (!matches)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Reduces a route to the path the contract compares: absolute path, no query, no trailing slash.</summary>
    private static string Normalize(string route)
    {
        var path = ProtoHttpUri.TryCreateAbsoluteHttpUri(route, out var absoluteUri)
            ? absoluteUri!.AbsolutePath
            : route.Split('#', 2)[0].Split('?', 2)[0];
        path = path.Trim();
        if (!path.StartsWith('/')) path = $"/{path}";
        return path.Length > 1 ? path.TrimEnd('/') : path;
    }
}
