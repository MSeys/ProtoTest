namespace ProtoTest.Core;

/// <summary>
/// Base type for runner-independent assertion failures produced by ProtoTest integrations.
/// </summary>
public abstract class ProtoAssertionException : Exception
{
    protected ProtoAssertionException(string message)
        : base(message)
    {
    }

    protected ProtoAssertionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
