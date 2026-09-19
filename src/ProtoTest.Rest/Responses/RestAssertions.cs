namespace ProtoTest.Rest;

using System.Net;

/// <summary>
/// The status assertions reachable through <see cref="RestResponse.Should"/> and
/// <see cref="RestResponse.ShouldNot"/>. Both sides run the same implementation; the negated side
/// asks for the opposite and its failure message, trace name, and Checks section read with "not".
/// </summary>
public sealed class RestAssertions
{
    private readonly RestResponse _response;
    private readonly bool _negated;

    internal RestAssertions(RestResponse response, bool negated)
    {
        _response = response;
        _negated = negated;
    }

    /// <summary>Asserts the response status; the negated form asserts it is anything but <paramref name="expected"/>.</summary>
    public RestResponse HaveHttpStatus(HttpStatusCode expected)
        => _response.AssertHttpStatus(expected, _negated);
}
