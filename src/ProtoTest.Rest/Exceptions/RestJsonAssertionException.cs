namespace ProtoTest.Rest.Exceptions;

using ProtoTest.Core;

/// <summary>Represents an assertion that could not inspect an invalid JSON response.</summary>
public sealed class RestJsonAssertionException : ProtoAssertionException
{
    public RestJsonAssertionException(string message, string responseBody)
        : base(message)
    {
        ResponseBody = responseBody ?? string.Empty;
    }

    public RestJsonAssertionException(string message, string responseBody, Exception innerException)
        : base(message, innerException)
    {
        ResponseBody = responseBody ?? string.Empty;
    }

    public string ResponseBody { get; }
}
