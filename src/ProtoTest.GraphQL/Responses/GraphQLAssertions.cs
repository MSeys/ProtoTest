namespace ProtoTest.GraphQL;

using System.Net;

/// <summary>
/// The HTTP status assertions reachable through <see cref="GraphQLResponse.Should"/> and
/// <see cref="GraphQLResponse.ShouldNot"/>. Both sides run the same implementation; the negated side
/// asks for the opposite and its failure message, trace name, and Checks section read with "not".
/// </summary>
public sealed class GraphQLAssertions
{
    private readonly GraphQLResponse _response;
    private readonly bool _negated;

    internal GraphQLAssertions(GraphQLResponse response, bool negated)
    {
        _response = response;
        _negated = negated;
    }

    /// <summary>Asserts the HTTP status; the negated form asserts it is anything but <paramref name="expected"/>.</summary>
    public GraphQLResponse HaveHttpStatus(HttpStatusCode expected)
        => _response.AssertHttpStatus(expected, _negated);
}
