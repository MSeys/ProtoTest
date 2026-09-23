namespace ProtoTest.Core;

/// <summary>Who disposes a client registered for a test.</summary>
public enum ProtoClientOwnership
{
    /// <summary>The execution context releases the client when the test completes.</summary>
    Context,

    /// <summary>The client is shared; its lifetime is managed elsewhere.</summary>
    Caller
}
