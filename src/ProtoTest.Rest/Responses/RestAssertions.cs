namespace ProtoTest.Rest;

using System.Net;
using ProtoTest.Http;

/// <summary>
/// The status assertions reachable through <see cref="RestResponse.Should"/> and
/// <see cref="RestResponse.ShouldNot"/>; the shared facade owns the polarity.
/// </summary>
public sealed class RestAssertions : ProtoHttpAssertions<RestResponse, RestAssertions>
{
    internal RestAssertions(RestResponse response, bool negated)
        : base(response, negated)
    {
    }

    /// <inheritdoc />
    protected override RestResponse AssertStatus(HttpStatusCode expected, bool negated)
        => Response.AssertHttpStatus(expected, negated);
}
