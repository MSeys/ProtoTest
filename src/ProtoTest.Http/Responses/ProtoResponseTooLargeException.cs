namespace ProtoTest.Http;

public sealed class ProtoResponseTooLargeException(int maximumBytes, long observedBytes)
    : Exception($"The response exceeded the configured limit of {maximumBytes} bytes (observed at least {observedBytes} bytes).")
{
    public int MaximumBytes { get; } = maximumBytes;
    public long ObservedBytes { get; } = observedBytes;
}
