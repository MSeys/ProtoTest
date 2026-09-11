namespace ProtoTest.Rest.Matching;

using System.Text.Json;
using ProtoTest.Rest.Exceptions;

internal static class ShapeMatcher
{
    public static IReadOnlyList<string> AssertMatch(
        string jsonContent,
        object expectedShape,
        JsonSerializerOptions? options = null)
    {
        try
        {
            return ProtoTest.Json.JsonShapeMatcher.AssertMatch(jsonContent, expectedShape, options);
        }
        catch (ProtoTest.Json.JsonDocumentAssertionException exception)
        {
            throw exception.InnerException is null
                ? new RestJsonAssertionException(exception.Message, exception.Content)
                : new RestJsonAssertionException(exception.Message, exception.Content, exception.InnerException);
        }
        catch (ProtoTest.Json.JsonShapeMismatchException exception)
        {
            throw new ShapeMismatchException(exception.Mismatches
                .Select(mismatch => new ShapeMismatch(
                    mismatch.PropertyPath,
                    mismatch.Reason,
                    mismatch.Expected,
                    mismatch.Actual))
                .ToArray());
        }
    }
}
