namespace ProtoTest.GraphQL;

using System.Text.Json;
using ProtoTest.Json;

public sealed record GraphQLShapeMismatch(string Path, string Reason, object? Expected, object? Actual);

public sealed class GraphQLShapeMismatchException(IReadOnlyList<GraphQLShapeMismatch> mismatches)
    : ProtoTest.Core.ProtoAssertionException(BuildMessage(mismatches))
{
    public IReadOnlyList<GraphQLShapeMismatch> Mismatches { get; } = [.. mismatches];

    private static string BuildMessage(IReadOnlyList<GraphQLShapeMismatch> mismatches)
        => $"GraphQL data shape mismatch with {mismatches.Count} error(s):{Environment.NewLine}" +
           string.Join(Environment.NewLine, mismatches.Select(m => $"  • [{m.Path}]: {m.Reason}"));
}

internal static class GraphQLShapeMatcher
{
    public static IReadOnlyList<string> AssertMatch(JsonElement actual, object expected, JsonSerializerOptions? options)
    {
        try
        {
            return JsonShapeMatcher.AssertMatch(actual, expected, options);
        }
        catch (JsonShapeMismatchException exception)
        {
            throw new GraphQLShapeMismatchException(exception.Mismatches
                .Select(mismatch => new GraphQLShapeMismatch(
                    mismatch.PropertyPath,
                    mismatch.Reason,
                    mismatch.Expected,
                    mismatch.Actual))
                .ToArray());
        }
    }
}
