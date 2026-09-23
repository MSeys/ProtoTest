namespace ProtoTest.TUnit;

using ProtoTest.Core;

/// <summary>
/// Abstract base class for TUnit assembly configuration.
/// Manages the root <see cref="ProtoHost"/> lifecycle for the assembly.
/// </summary>
public abstract class ProtoTestAssembly
    : ProtoTestAssemblyHost<ProtoTestAssembly>, IProtoTestAssemblyHost<ProtoTestAssembly>
{
    static string IProtoTestAssemblyHost<ProtoTestAssembly>.UninitializedHint =>
        "Ensure your setup class inherits from ProtoTestAssembly.";

    /// <summary>
    /// Builds and starts the global <see cref="ProtoHost"/> instance asynchronously.
    /// </summary>
    /// <param name="configure">Delegate to configure the <see cref="IProtoHostBuilder"/>.</param>
    /// <exception cref="InvalidOperationException">Thrown if the host has already been initialized.</exception>
    protected static Task InitializeAsync(Action<IProtoHostBuilder> configure)
        => StartAsync(configure);

    /// <summary>
    /// Stops and disposes the global <see cref="ProtoHost"/> instance asynchronously.
    /// </summary>
    protected static Task CleanupAsync() => StopAsync();
}
