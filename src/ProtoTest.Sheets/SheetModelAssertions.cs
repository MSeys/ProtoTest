namespace ProtoTest.Sheets;

using System.Text.Json;
using ProtoTest.Json;

/// <summary>
/// Row-level shape assertions for record models, reusing the Json shape matcher the REST and GraphQL
/// integrations use. A row record serializes with its property names, so an anonymous expected shape
/// reads the same way as elsewhere.
/// </summary>
public static class SheetModelAssertions
{
    /// <summary>Matches one model row against an expected shape.</summary>
    public static void ShouldMatchShape<TRow>(this TRow row, object expectedShape, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(expectedShape);
        var actual = JsonSerializer.SerializeToElement(row, options);
        try
        {
            // The matcher returns the matched paths and throws on any mismatch.
            _ = JsonShapeMatcher.AssertMatch(actual, expectedShape, options);
        }
        catch (Core.ProtoAssertionException exception)
        {
            throw new SpreadsheetAssertionException(exception.Message);
        }
    }
}
