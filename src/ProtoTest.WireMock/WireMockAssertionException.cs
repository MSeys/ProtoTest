namespace ProtoTest.WireMock;

using ProtoTest.Core;

/// <summary>Thrown when a WireMock fake assertion fails, naming the fake and the requests behind it.
/// Derives from <see cref="ProtoAssertionException"/>, so a protocol assertion failure is catchable
/// as the shared type.</summary>
public sealed class WireMockAssertionException : ProtoAssertionException
{
    public WireMockAssertionException(string message)
        : base(message)
    {
    }

    public WireMockAssertionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
