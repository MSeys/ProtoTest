namespace ProtoTest.Rest.Exceptions;

/// <summary>Indicates that a response exceeded the configured buffering limit.</summary>
public sealed class RestResponseTooLargeException : Exception
{
    public RestResponseTooLargeException(int maximumBytes, long observedBytes)
        : base($"The REST response exceeded the configured limit of {maximumBytes} bytes (observed at least {observedBytes} bytes).")
    {
        MaximumBytes = maximumBytes;
        ObservedBytes = observedBytes;
    }

    public int MaximumBytes { get; }
    public long ObservedBytes { get; }
}
