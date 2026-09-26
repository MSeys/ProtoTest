namespace ProtoTest.GraphQL;

using System.Net;
using System.Text.Json;
using ProtoTest.Http;

/// <summary>
/// The assertions reachable through <see cref="GraphQLResponse.Should"/> and
/// <see cref="GraphQLResponse.ShouldNot"/>; the shared facade owns the polarity, so a negated error
/// assertion asks for the opposite of its positive spelling.
/// </summary>
public class GraphQLAssertions : ProtoHttpAssertions<GraphQLResponse>
{
    internal GraphQLAssertions(GraphQLResponse response, bool negated)
        : base(response, negated)
    {
    }

    /// <inheritdoc />
    protected override GraphQLResponse AssertStatus(HttpStatusCode expected, bool negated)
        => Response.AssertHttpStatus(expected, negated);

    /// <summary>
    /// Asserts the response carries no GraphQL errors; on <see cref="GraphQLResponse.ShouldNot"/> it
    /// asserts the response does carry errors.
    /// </summary>
    public GraphQLResponse HaveNoErrors() => Response.AssertNoErrors(Negated);

    /// <summary>
    /// Asserts the response carries at least one GraphQL error; on
    /// <see cref="GraphQLResponse.ShouldNot"/> it asserts the response carries none.
    /// </summary>
    public GraphQLResponse HaveErrors() => Response.AssertHasErrors(Negated);

    /// <summary>
    /// Asserts the response carries a GraphQL error whose <c>extensions.code</c> is
    /// <paramref name="code"/>, compared case-insensitively; on
    /// <see cref="GraphQLResponse.ShouldNot"/> it asserts no error carries that code.
    /// </summary>
    public GraphQLResponse HaveError(string code) => Response.AssertHasError(code, Negated);
}

/// <summary>
/// The positive assertions of a GraphQL response, reached through <see cref="GraphQLResponse.Should"/>.
/// Shape is positive-only - a negated shape match has no meaning - so it lives here and not on the
/// shared facade <see cref="GraphQLResponse.ShouldNot"/> returns. <see cref="MatchShape"/> returns the
/// response, so assertions chain.
/// </summary>
public sealed class GraphQLShouldAssertions : GraphQLAssertions
{
    internal GraphQLShouldAssertions(GraphQLResponse response)
        : base(response, negated: false)
    {
    }

    /// <summary>
    /// Matches the selected data against the expected shape and records the assertion on the test
    /// context. Every mismatch is reported at once, and the failure message starts with the operation
    /// identifier. Returns the response.
    /// </summary>
    public GraphQLResponse MatchShape(object expectedShape, JsonSerializerOptions? options = null)
        => Response.AssertResponseShape(expectedShape, options);
}
