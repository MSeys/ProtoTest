namespace ProtoTest.Data;

/// <summary>Thrown when test data cannot be constructed unambiguously.</summary>
public sealed class ProtoDataException : InvalidOperationException
{
    public ProtoDataException(string message) : base(message) { }
    public ProtoDataException(string message, Exception innerException) : base(message, innerException) { }
}
