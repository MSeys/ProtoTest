namespace ProtoTest.AspNetCore.Internal;

using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Best-effort inventory of an in-process application's page-like GET routes. Each route becomes a
/// <c>web.page.available</c> observation, so a page that exists but was never visited shows as
/// uncovered. The filter is deliberately conservative:
/// <list type="bullet">
/// <item>Razor Page endpoints, non-API MVC actions, and endpoints that produce <c>text/html</c> count as pages.</item>
/// <item>API-shaped prefixes (<c>/api</c>, <c>/graphql</c>, framework <c>/_…</c> routes and similar) are excluded.</item>
/// <item>Parameterized and catch-all templates (<c>{id}</c>, <c>{**path}</c>) are excluded, because a route
/// pattern cannot be matched by a concrete visited path; inventory is for stable page paths.</item>
/// <item>Only endpoints that explicitly declare GET are considered; an endpoint with no method
/// metadata is not inventoried as a page.</item>
/// </list>
/// Include and exclude globs can refine the result per application with
/// <c>ProtoTest:Applications:{application}:Web:Pages:Include</c> and <c>…:Exclude</c>.
/// </summary>
internal static class AspNetCorePageInventory
{
    private static readonly string[] ApiRoutePrefixes =
    [
        "/api",
        "/graphql",
        "/odata",
        "/swagger",
        "/openapi",
        "/health",
        "/metrics",
        "/hubs",
        "/.well-known",
        "/_"
    ];

    /// <summary>Discovers the page-like route paths of the started application.</summary>
    public static IReadOnlyList<string> Discover(
        IServiceProvider services,
        IConfiguration configuration,
        string applicationName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        var include = ReadGlobs(configuration, applicationName, "Include");
        var exclude = ReadGlobs(configuration, applicationName, "Exclude");
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var endpoint in EnumerateEndpoints(services))
        {
            if (endpoint is not RouteEndpoint route) continue;
            var pattern = route.RoutePattern.RawText;
            if (string.IsNullOrWhiteSpace(pattern)) continue;
            if (pattern.Contains('{')) continue;
            var path = NormalizePath(pattern);
            if (path is null) continue;
            if (!IsGet(endpoint)) continue;
            // An API-shaped route is excluded unless the endpoint is page-like beyond JSON: an
            // [ApiController] action that renders a view or produces HTML is still a page.
            if (IsApiShaped(path) && !HasHtmlEvidence(endpoint) && !ReturnsViewResultFor(endpoint)) continue;
            if (!IsPageLike(endpoint)) continue;
            if (include.Length > 0 && !include.Any(glob => GlobMatch(glob, path))) continue;
            if (exclude.Any(glob => GlobMatch(glob, path))) continue;
            paths.Add(path);
        }

        return [.. paths];
    }

    private static IEnumerable<Endpoint> EnumerateEndpoints(IServiceProvider services)
    {
        var sources = new List<EndpointDataSource>();
        sources.AddRange(services.GetServices<EndpointDataSource>());
        if (services.GetService<EndpointDataSource>() is { } registered && !sources.Contains(registered))
        {
            sources.Add(registered);
        }

        if (services.GetService<IEndpointRouteBuilder>() is { } routeBuilder)
        {
            sources.AddRange(routeBuilder.DataSources);
        }

        var seen = new HashSet<Endpoint>(ReferenceEqualityComparer.Instance);
        foreach (var source in sources.Distinct())
        {
            foreach (var endpoint in source.Endpoints)
            {
                if (seen.Add(endpoint)) yield return endpoint;
            }
        }
    }

    /// <summary>
    /// A Razor Page is a page. An MVC controller action is a page only with evidence that it renders
    /// HTML: <see cref="ProducesAttribute"/> metadata naming <c>text/html</c>, or a view result return
    /// type. A JSON controller action is not a page, and an <c>[ApiController]</c> action that produces
    /// HTML is one.
    /// </summary>
    private static bool IsPageLike(Endpoint endpoint)
    {
        if (endpoint.Metadata.GetMetadata<PageActionDescriptor>() is not null) return true;
        if (endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is { } action)
        {
            return HasHtmlEvidence(endpoint) || ReturnsViewResult(action.MethodInfo);
        }

        return ProducesHtml(endpoint);
    }

    /// <summary>Whether the endpoint declares an HTML response, on the endpoint or its controller/action.</summary>
    private static bool HasHtmlEvidence(Endpoint endpoint)
    {
        if (ProducesHtml(endpoint)) return true;
        return endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is { } action
               && (ProducesHtml(action.MethodInfo) || ProducesHtml(action.ControllerTypeInfo));
    }

    /// <summary>Whether the endpoint is an MVC action returning a view result.</summary>
    private static bool ReturnsViewResultFor(Endpoint endpoint)
        => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is { } action
           && ReturnsViewResult(action.MethodInfo);

    /// <summary>
    /// Whether the action returns a view result. The check walks the return type's base chain by name
    /// because this package does not reference the MVC ViewFeatures assembly at compile time, and the
    /// hierarchy has varied across MVC versions (<c>ViewResult</c> derived from <c>ViewResultBase</c> or
    /// directly from <c>ActionResult</c>).
    /// </summary>
    private static bool ReturnsViewResult(MethodInfo method)
    {
        for (var type = Unwrap(method.ReturnType); type is not null; type = type.BaseType)
        {
            if (type.FullName is "Microsoft.AspNetCore.Mvc.ViewResult"
                or "Microsoft.AspNetCore.Mvc.PartialViewResult"
                or "Microsoft.AspNetCore.Mvc.ViewResultBase")
            {
                return true;
            }
        }

        return false;
    }

    private static Type Unwrap(Type type)
        => type.IsGenericType
           && (type.GetGenericTypeDefinition() == typeof(Task<>) || type.GetGenericTypeDefinition() == typeof(ValueTask<>))
            ? type.GetGenericArguments()[0]
            : type;

    private static bool ProducesHtml(Endpoint endpoint)
        => endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Any(metadata => metadata.ContentTypes.Any(IsHtml));

    private static bool ProducesHtml(MemberInfo member)
        => member.GetCustomAttributes<ProducesAttribute>(inherit: true)
            .Any(attribute => attribute.ContentTypes.Any(IsHtml));

    private static bool IsHtml(string contentType)
        => contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase);

    /// <summary>Only an endpoint that explicitly declares GET is a page; no method metadata is not GET.</summary>
    private static bool IsGet(Endpoint endpoint)
    {
        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
        return methods is { Count: > 0 }
               && methods.Any(method => string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsApiShaped(string path)
        => ApiRoutePrefixes.Any(prefix =>
            path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase));

    private static string? NormalizePath(string pattern)
    {
        var value = pattern.Trim();
        if (value.Length == 0) return null;
        if (!value.StartsWith('/')) value = "/" + value;
        if (value.Length > 1) value = value.TrimEnd('/');
        return value.Length == 0 ? "/" : value;
    }

    private static string[] ReadGlobs(IConfiguration configuration, string applicationName, string name)
    {
        var section = configuration.GetSection($"ProtoTest:Applications:{applicationName}:Web:Pages:{name}");
        var values = section.GetChildren()
            .Select(child => child.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToList();
        // A single Include/Exclude value is a scalar in configuration; the web collector reads the
        // same section the same way, so the two inventories cannot disagree.
        if (values.Count == 0 && !string.IsNullOrWhiteSpace(section.Value))
        {
            values.Add(section.Value!);
        }

        return [.. values];
    }

    /// <summary>Case-insensitive glob match: <c>*</c> is any run of characters, <c>?</c> is exactly one.</summary>
    internal static bool GlobMatch(string pattern, string value)
    {
        int patternIndex = 0, valueIndex = 0, starIndex = -1, mark = 0;
        while (valueIndex < value.Length)
        {
            if (patternIndex < pattern.Length
                && (pattern[patternIndex] == '?'
                    || char.ToLowerInvariant(pattern[patternIndex]) == char.ToLowerInvariant(value[valueIndex])))
            {
                patternIndex++;
                valueIndex++;
            }
            else if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                starIndex = patternIndex++;
                mark = valueIndex;
            }
            else if (starIndex >= 0)
            {
                patternIndex = starIndex + 1;
                valueIndex = ++mark;
            }
            else
            {
                return false;
            }
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*') patternIndex++;
        return patternIndex == pattern.Length;
    }
}
