namespace ProtoTest.WireMock;

/// <summary>Thrown when a WireMock fake assertion fails, naming the fake and the requests behind it.</summary>
public sealed class WireMockAssertionException : Exception
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
