namespace ProtoTest.Rest;

using System.Net;
using System.Text.Json;
using ProtoTest.Http;

/// <summary>
/// The status assertions reachable through <see cref="RestResponse.Should"/> and
/// <see cref="RestResponse.ShouldNot"/>; the shared facade owns the polarity.
/// </summary>
public class RestAssertions : ProtoHttpAssertions<RestResponse, RestAssertions>
{
    internal RestAssertions(RestResponse response, bool negated)
        : base(response, negated)
    {
    }

    /// <inheritdoc />
    protected override RestResponse AssertStatus(HttpStatusCode expected, bool negated)
        => Response.AssertHttpStatus(expected, negated);
}

/// <summary>
/// The positive assertions of a REST response, reached through <see cref="RestResponse.Should"/>.
/// Shape is positive-only - a negated shape match has no meaning - so it lives here and not on the
/// shared facade <see cref="RestResponse.ShouldNot"/> returns. <see cref="MatchShape"/> returns the
/// response, so assertions chain.
/// </summary>
public sealed class RestShouldAssertions : RestAssertions
{
    internal RestShouldAssertions(RestResponse response)
        : base(response, negated: false)
    {
    }

    /// <summary>
    /// Matches the response body against the expected shape and records the assertion on the test
    /// context. Every mismatch is reported at once, and the failure message starts with the request
    /// identifier. Returns the response.
    /// </summary>
    public RestResponse MatchShape(object expectedShape, JsonSerializerOptions? options = null)
        => Response.AssertResponseShape(expectedShape, options);
}
