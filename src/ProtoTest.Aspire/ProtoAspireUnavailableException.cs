namespace ProtoTest.Aspire;

/// <summary>
/// Thrown when the Aspire orchestration runtime itself is unavailable: the DCP executable or the
/// dashboard binaries the AppHost needs to start could not be found. A suite catches this to skip
/// with a reason instead of failing, the way container suites skip without a runtime.
/// </summary>
public sealed class ProtoAspireUnavailableException : InvalidOperationException
{
    /// <summary>Creates the exception with the reason the runtime is unavailable.</summary>
    public ProtoAspireUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with the reason and the failure that proved it.</summary>
    public ProtoAspireUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
