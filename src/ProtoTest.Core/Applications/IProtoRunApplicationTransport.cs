namespace ProtoTest.Core;

/// <summary>
/// Serves an application that has no address to the run itself, before any test: an in-process server
/// implements it. <see cref="ProtoRunSetupContext.ApplicationClientAsync"/> uses it when the application
/// has no configured or published address.
/// </summary>
public interface IProtoRunApplicationTransport
{
    /// <summary>The application the transport serves.</summary>
    string ApplicationName { get; }

    /// <summary>Opens a client for the application; the run releases it when the phase ends.</summary>
    ValueTask<ProtoRunApplicationClient> OpenRunClientAsync(ProtoRunSetupContext context);
}

/// <summary>A client the run opened for an application, and what was started only for it.</summary>
/// <param name="Client">The client, with the application's base address.</param>
/// <param name="Owned">Released with the client, such as a server started only for this phase.</param>
public sealed record ProtoRunApplicationClient(HttpClient Client, IAsyncDisposable? Owned = null);
