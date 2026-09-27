namespace ProtoTest.Rest;

using System.Text.Json;

/// <summary>
/// The in-call shape expectation for a REST request: await the pending response and match it against
/// the expected shape in one expression, so the shape the test demands is stated where the call is
/// made. It is the same facade assertion <c>response.Should.MatchShape(shape)</c> performs, so there is
/// one shape implementation behind both spellings.
/// </summary>
public static class RestResponseExpectations
{
    /// <summary>
    /// Awaits the pending response and matches it against <paramref name="expectedShape"/> through
    /// <see cref="RestShouldAssertions.MatchShape(object, JsonSerializerOptions?)"/>, returning the
    /// response. On a mismatch the response is disposed and the
    /// <see cref="Exceptions.RestAssertionException"/> is rethrown; <paramref name="exact"/> also
    /// demands that the response carries no field the shape does not mention.
    /// </summary>
    public static async Task<RestResponse> ExpectAsync<TShape>(
        this Task<RestResponse> responseTask,
        TShape expectedShape,
        JsonSerializerOptions? options = null,
        bool exact = false)
    {
        ArgumentNullException.ThrowIfNull(responseTask);
        ArgumentNullException.ThrowIfNull(expectedShape);
        var response = await responseTask.ConfigureAwait(false);
        try
        {
            response.Should.MatchShape(expectedShape, exact, options);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }
}
