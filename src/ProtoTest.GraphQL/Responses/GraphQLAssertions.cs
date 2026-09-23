namespace ProtoTest.GraphQL;

using System.Net;
using ProtoTest.Http;

/// <summary>
/// The HTTP status assertions reachable through <see cref="GraphQLResponse.Should"/> and
/// <see cref="GraphQLResponse.ShouldNot"/>; the shared facade owns the polarity.
/// </summary>
public sealed class GraphQLAssertions : ProtoHttpAssertions<GraphQLResponse, GraphQLAssertions>
{
    internal GraphQLAssertions(GraphQLResponse response, bool negated)
        : base(response, negated)
    {
    }

    /// <inheritdoc />
    protected override GraphQLResponse AssertStatus(HttpStatusCode expected, bool negated)
        => Response.AssertHttpStatus(expected, negated);
}
