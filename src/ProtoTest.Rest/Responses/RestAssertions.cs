namespace ProtoTest.Rest;

using System.Net;
using System.Text.Json;
using ProtoTest.Http;

/// <summary>
/// The status assertions reachable through <see cref="RestResponse.Should"/> and
/// <see cref="RestResponse.ShouldNot"/>; the shared facade owns the polarity.
/// </summary>
public class RestAssertions : ProtoHttpAssertions<RestResponse>
{
    internal RestAssertions(RestResponse response, bool negated)
        : base(response, negated)
    {
    }

    /// <inheritdoc />
    protected override RestResponse AssertStatus(HttpStatusCode expected, bool negated)
        => Response.AssertHttpStatus(expected, negated);

    /// <summary>
    /// Asserts the response body's content type, comparing its media type without parameters
    /// (case-insensitively); on <see cref="RestResponse.ShouldNot"/> it asserts the response carries
    /// anything but that media type. Returns the response.
    /// </summary>
    public RestResponse HaveContentType(string mediaType) => Response.AssertContentType(mediaType, Negated);

    /// <summary>
    /// Asserts the response carries a header with <paramref name="name"/>; on
    /// <see cref="RestResponse.ShouldNot"/> it asserts the header is absent. Returns the response.
    /// </summary>
    public RestResponse HaveHeader(string name) => Response.AssertHeader(name, expectedValue: null, Negated);

    /// <summary>
    /// Asserts the response carries a header with <paramref name="name"/> and a value equal to
    /// <paramref name="value"/> (ordinal; any value of a multi-valued header matches); on
    /// <see cref="RestResponse.ShouldNot"/> it asserts no such value is present. Returns the response.
    /// </summary>
    public RestResponse HaveHeader(string name, string value) => Response.AssertHeader(name, value, Negated);

    /// <summary>
    /// Asserts the response sets a cookie with <paramref name="name"/> (case-sensitive, as cookie
    /// names are); on <see cref="RestResponse.ShouldNot"/> it asserts the cookie is not set. Returns
    /// the response.
    /// </summary>
    public RestResponse HaveCookie(string name) => Response.AssertCookie(name, expectedValue: null, Negated);

    /// <summary>
    /// Asserts the response sets a cookie with <paramref name="name"/> and a value equal to
    /// <paramref name="value"/> (ordinal); on <see cref="RestResponse.ShouldNot"/> it asserts no such
    /// value is set. Returns the response.
    /// </summary>
    public RestResponse HaveCookie(string name, string value) => Response.AssertCookie(name, value, Negated);

    /// <summary>
    /// Asserts the response's <c>Location</c> header equals <paramref name="location"/> as it arrived
    /// (ordinal, relative or absolute); on <see cref="RestResponse.ShouldNot"/> it asserts the header
    /// carries anything but that location. Returns the response.
    /// </summary>
    public RestResponse HaveRedirectLocation(string location) => Response.AssertRedirectLocation(location, Negated);
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

    /// <summary>
    /// Matches the response body against the expected shape exactly: a field present in the body that
    /// the shape does not mention is a mismatch naming the field, so the response cannot grow a field
    /// the test never asserted. A value constraint mentions its whole subtree. Returns the response.
    /// </summary>
    public RestResponse MatchShape(object expectedShape, bool exact, JsonSerializerOptions? options = null)
        => Response.AssertResponseShape(expectedShape, options, exact);
}
