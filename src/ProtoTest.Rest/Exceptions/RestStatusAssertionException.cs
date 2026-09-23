namespace ProtoTest.Rest.Exceptions;

using System.Net;
using ProtoTest.Core;
using ProtoTest.Http;

/// <summary>
/// Represents a failed assertion against an HTTP response status code. A negated assertion states
/// that the status must not equal the expected value, so its message reads with "not".
/// </summary>
public sealed class RestStatusAssertionException : ProtoAssertionException
{
    public RestStatusAssertionException(
        HttpStatusCode expectedStatusCode,
        HttpStatusCode actualStatusCode,
        string? responseBody)
        : this(expectedStatusCode, actualStatusCode, responseBody, negated: false)
    {
    }

    public RestStatusAssertionException(
        HttpStatusCode expectedStatusCode,
        HttpStatusCode actualStatusCode,
        string? responseBody,
        bool negated)
        : base(ProtoStatusAssertion.DescribeFailure(expectedStatusCode, actualStatusCode, negated, responseBody))
    {
        ExpectedStatusCode = expectedStatusCode;
        ActualStatusCode = actualStatusCode;
        ResponseBody = responseBody ?? string.Empty;
        Negated = negated;
    }

    public HttpStatusCode ExpectedStatusCode { get; }
    public HttpStatusCode ActualStatusCode { get; }
    public string ResponseBody { get; }

    /// <summary>Whether the failed assertion asked for a status other than <see cref="ExpectedStatusCode"/>.</summary>
    public bool Negated { get; }
}
