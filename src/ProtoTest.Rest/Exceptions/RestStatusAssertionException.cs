namespace ProtoTest.Rest.Exceptions;

using System.Net;
using ProtoTest.Core;

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
        : base(BuildMessage(expectedStatusCode, actualStatusCode, responseBody, negated))
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

    private static string BuildMessage(
        HttpStatusCode expectedStatusCode,
        HttpStatusCode actualStatusCode,
        string? responseBody,
        bool negated)
    {
        var expectation = ProtoAssertion.Describe(
            $"{(int)expectedStatusCode} ({expectedStatusCode})",
            negated);
        var message =
            $"Expected HTTP status {expectation}, " +
            $"but received {(int)actualStatusCode} ({actualStatusCode}).";

        return string.IsNullOrEmpty(responseBody)
            ? message
            : $"{message}{Environment.NewLine}Response body:{Environment.NewLine}{responseBody}";
    }
}
