namespace ProtoTest.Diagnosis;

using System.Text.RegularExpressions;
using ProtoTest.Traces;

/// <summary>What to do about an uncovered unit.</summary>
public static class ProtoCoverageActions
{
    /// <summary>A test already calls the unit's endpoint; it can check the uncovered part.</summary>
    public const string Extend = "extend";

    /// <summary>No test calls the endpoint; a new test is needed, shaped like the named one.</summary>
    public const string New = "new";
}

/// <summary>
/// One uncovered unit and the test to start from: the one that already calls its endpoint (extend it),
/// or the closest one that calls the same resource or the same kind of call (write a new test like it).
/// </summary>
public sealed record ProtoCoverageSuggestion(
    string Target,
    string Category,
    string Identifier,
    string? Endpoint,
    string Action,
    string? Test,
    string? SourceFile,
    string Reason)
{
    /// <summary>The unit's path in the report, the name run verification gives it (<c>GET /a › 200 › $.id</c>).</summary>
    public string Path { get; init; } = Identifier;
}

/// <summary>
/// Turns a run's uncovered units into suggestions from what the tests recorded. A unit nested under an
/// endpoint (a response or a property) belongs to that endpoint. Recorded paths carry ids
/// (<c>/sessions/42</c>) and units carry templates (<c>/sessions/{id}</c>), so a template matches any
/// value in its parameter segments.
/// </summary>
internal static partial class ProtoCoverageSuggester
{
    public static IReadOnlyList<ProtoCoverageSuggestion> Suggest(ProtoTraceArchive archive, ProtoTraceReport report)
    {
        var calls = archive.Tests
            .Where(test => test.Succeeded)
            .SelectMany(test => test.Operations
                .Where(operation => (ProtoTraceOperation.IsCall(operation.Kind) && operation.Subject is { Length: > 0 })
                    || operation.Kind == Navigate)
                .Select(operation => (Test: test, Operation: operation)))
            .ToList();
        var suggestions = new List<ProtoCoverageSuggestion>();
        foreach (var item in report.Items)
        {
            Walk(item, endpoint: null, parent: null, parentPath: null, calls, suggestions);
        }

        return suggestions;
    }

    private static void Walk(
        ProtoTraceReportItem item,
        string? endpoint,
        ProtoTraceReportItem? parent,
        string? parentPath,
        List<(ProtoTraceTest Test, ProtoTraceOperation Operation)> calls,
        List<ProtoCoverageSuggestion> suggestions)
    {
        if (Endpoint().IsMatch(item.Identifier))
        {
            endpoint = item.Identifier;
        }

        var path = ProtoTraceCoverageUnit.Combine(parentPath, parent?.Identifier, item.Identifier);
        if (string.Equals(item.Kind, "coverage", StringComparison.OrdinalIgnoreCase) && item.IsCovered is false)
        {
            suggestions.Add(Suggest(item, endpoint, calls) with { Path = path });
        }

        foreach (var child in item.Children ?? [])
        {
            Walk(child, endpoint, item, path, calls, suggestions);
        }
    }

    private static ProtoCoverageSuggestion Suggest(
        ProtoTraceReportItem item,
        string? endpoint,
        List<(ProtoTraceTest Test, ProtoTraceOperation Operation)> calls)
    {
        var key = endpoint ?? item.Identifier;
        var unit = endpoint is null || endpoint == item.Identifier ? $"'{key}'" : $"'{item.Identifier}' of '{endpoint}'";
        var (method, path) = Split(key);

        if (method is null && item.Identifier.StartsWith('/'))
        {
            return SuggestPage(item, endpoint, calls);
        }

        calls = [.. calls.Where(call => call.Operation.Subject is not null)];
        if (calls.FirstOrDefault(call => Matches(call.Operation.Subject!, method, path, sameMethod: true)) is { Test: not null } exact)
        {
            return Create(item, endpoint, ProtoCoverageActions.Extend, exact.Test, exact.Operation,
                $"'{exact.Test.Name}' already calls {key}; check {unit} there.");
        }

        if (path is not null && calls.FirstOrDefault(call => Matches(call.Operation.Subject!, method, path, sameMethod: false)) is { Test: not null } sibling)
        {
            return Create(item, endpoint, ProtoCoverageActions.New, sibling.Test, sibling.Operation,
                $"No test calls {key}. '{sibling.Test.Name}' calls {sibling.Operation.Subject} on the same path; write a new test shaped like it.");
        }

        if (path is not null && calls.FirstOrDefault(call => SameResource(call.Operation.Subject!, path)) is { Test: not null } neighbour)
        {
            return Create(item, endpoint, ProtoCoverageActions.New, neighbour.Test, neighbour.Operation,
                $"No test calls {key}. '{neighbour.Test.Name}' calls {neighbour.Operation.Subject} on the same resource; write a new test shaped like it.");
        }

        var kind = path is not null ? "http.request" : null;
        var example = calls.FirstOrDefault(call => kind is null || call.Operation.Kind == kind);
        return example.Test is null
            ? Create(item, endpoint, ProtoCoverageActions.New, null, null, $"No test reaches {unit}, and the run recorded no call to start from.")
            : Create(item, endpoint, ProtoCoverageActions.New, example.Test, example.Operation,
                $"No test reaches {unit}. '{example.Test.Name}' is a recorded call of the same kind; write a new test shaped like it.");
    }

    // A page unit is a route a browser test opens; the navigation records the URL in its name.
    private static ProtoCoverageSuggestion SuggestPage(
        ProtoTraceReportItem item,
        string? endpoint,
        List<(ProtoTraceTest Test, ProtoTraceOperation Operation)> calls)
    {
        var navigations = calls.Where(call => call.Operation.Kind == Navigate).ToList();
        var page = Template(item.Identifier);
        if (navigations.FirstOrDefault(call => NavigatedPath(call.Operation) is { } path && page.IsMatch(path)) is { Test: not null } opened)
        {
            return Create(item, endpoint, ProtoCoverageActions.Extend, opened.Test, opened.Operation,
                $"'{opened.Test.Name}' opens {item.Identifier} but checks nothing on it; assert on that page there so it counts as verified.");
        }

        return navigations.FirstOrDefault() is { Test: not null } journey
            ? Create(item, endpoint, ProtoCoverageActions.New, journey.Test, journey.Operation,
                $"No test opens {item.Identifier}. '{journey.Test.Name}' is a recorded browser journey; write a new test shaped like it.")
            : Create(item, endpoint, ProtoCoverageActions.New, null, null, $"No test opens {item.Identifier}, and the run recorded no browser journey to start from.");
    }

    private static string? NavigatedPath(ProtoTraceOperation operation)
    {
        var target = operation.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return target is null
            ? null
            : Uri.TryCreate(target, UriKind.Absolute, out var uri) ? uri.AbsolutePath : target.StartsWith('/') ? Strip(target) : null;
    }

    private const string Navigate = "web.navigate";

    private static ProtoCoverageSuggestion Create(
        ProtoTraceReportItem item,
        string? endpoint,
        string action,
        ProtoTraceTest? test,
        ProtoTraceOperation? operation,
        string reason)
        => new(item.TargetName, item.Category, item.Identifier, endpoint, action, test?.Name, operation?.SourceFile ?? TestFile(test), reason);

    // A navigation or a framework call may carry no location; the test method's own calls do.
    private static string? TestFile(ProtoTraceTest? test)
        => test?.Operations
            .FirstOrDefault(operation => operation.SourceFile is not null
                && operation.SourceFunction?.Contains(test.MethodName, StringComparison.Ordinal) == true)
            ?.SourceFile;

    private static (string? Method, string? Path) Split(string identifier)
    {
        var match = Endpoint().Match(identifier);
        return match.Success ? (match.Groups["method"].Value, match.Groups["path"].Value) : (null, null);
    }

    // A recorded subject matches a unit when its path fits the template; the method must match too when asked.
    private static bool Matches(string subject, string? method, string? path, bool sameMethod)
    {
        if (path is null)
        {
            return sameMethod && string.Equals(subject, method, StringComparison.Ordinal);
        }

        var (recordedMethod, recordedPath) = Split(subject);
        if (recordedPath is null)
        {
            return false;
        }

        var methodMatches = string.Equals(recordedMethod, method, StringComparison.OrdinalIgnoreCase);
        return (sameMethod ? methodMatches : !methodMatches) && Template(path).IsMatch(Strip(recordedPath));
    }

    // The same resource: the path up to its last fixed segment, so /sessions/{id}/invoice and /sessions/{id}/end share /sessions/{id}.
    private static bool SameResource(string subject, string path)
    {
        var (_, recordedPath) = Split(subject);
        if (recordedPath is null)
        {
            return false;
        }

        var segments = path.Trim('/').Split('/');
        var resource = string.Join('/', segments.Take(Math.Max(1, segments.Length - 1)));
        return Template("/" + resource).IsMatch(string.Join('/', Strip(recordedPath).Trim('/').Split('/').Take(Math.Max(1, segments.Length - 1))).Insert(0, "/"));
    }

    private static string Strip(string path)
    {
        var query = path.IndexOf('?', StringComparison.Ordinal);
        return query < 0 ? path : path[..query];
    }

    private static Regex Template(string path)
        => new("^" + string.Join("/", path.Split('/').Select(segment =>
            segment.StartsWith('{') && segment.EndsWith('}') ? "[^/]+" : Regex.Escape(segment))) + "/?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    [GeneratedRegex(@"^(?<method>GET|POST|PUT|PATCH|DELETE|HEAD|OPTIONS)\s+(?<path>/\S*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Endpoint();
}
