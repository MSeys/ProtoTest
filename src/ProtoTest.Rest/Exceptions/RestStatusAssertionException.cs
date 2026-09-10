namespace ProtoTest.Rest.Exceptions;

using System.Net;
using ProtoTest.Core;

/// <summary>
/// Represents a failed assertion against an HTTP response status code.
/// </summary>
public sealed class RestStatusAssertionException : ProtoAssertionException
{
    public RestStatusAssertionException(
        HttpStatusCode expectedStatusCode,
        HttpStatusCode actualStatusCode,
        string? responseBody)
        : base(BuildMessage(expectedStatusCode, actualStatusCode, responseBody))
    {
        ExpectedStatusCode = expectedStatusCode;
        ActualStatusCode = actualStatusCode;
        ResponseBody = responseBody ?? string.Empty;
    }

    public HttpStatusCode ExpectedStatusCode { get; }
    public HttpStatusCode ActualStatusCode { get; }
    public string ResponseBody { get; }

    private static string BuildMessage(
        HttpStatusCode expectedStatusCode,
        HttpStatusCode actualStatusCode,
        string? responseBody)
    {
        var message =
            $"Expected HTTP status {(int)expectedStatusCode} ({expectedStatusCode}), " +
            $"but received {(int)actualStatusCode} ({actualStatusCode}).";

        return string.IsNullOrEmpty(responseBody)
            ? message
            : $"{message}{Environment.NewLine}Response body:{Environment.NewLine}{responseBody}";
    }
}
