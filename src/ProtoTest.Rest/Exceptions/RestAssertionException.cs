namespace ProtoTest.Rest.Exceptions;

using ProtoTest.Core;

/// <summary>
/// Represents a failed REST assertion whose message names the request identifier it was made against.
/// A shape failure keeps the shared <c>JsonShapeMismatchException</c> as its inner exception, so the
/// mismatch list stays inspectable.
/// </summary>
public sealed class RestAssertionException : ProtoAssertionException
{
    public RestAssertionException(string message)
        : base(message)
    {
    }

    public RestAssertionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
