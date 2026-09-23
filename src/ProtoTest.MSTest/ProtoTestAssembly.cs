namespace ProtoTest.MSTest;

using ProtoTest.Core;

/// <summary>
/// Abstract base class for MSTest assembly configuration.
/// Manages the root <see cref="ProtoHost"/> lifecycle for the assembly.
/// </summary>
public abstract class ProtoTestAssembly
    : ProtoTestAssemblyHost<ProtoTestAssembly>, IProtoTestAssemblyHost<ProtoTestAssembly>
{
    static string IProtoTestAssemblyHost<ProtoTestAssembly>.UninitializedHint =>
        "Ensure your setup class inherits from ProtoTestAssembly and calls InitializeAsync().";

    /// <summary>
    /// Initializes and starts the global <see cref="ProtoHost"/> asynchronously for the test assembly.
    /// Call this inside your assembly-initialize method.
    /// </summary>
    /// <param name="configure">Delegate to configure services and hooks via <see cref="IProtoHostBuilder"/>.</param>
    /// <exception cref="InvalidOperationException">Thrown if the host has already been initialized.</exception>
    protected static Task InitializeAsync(Action<IProtoHostBuilder> configure)
        => StartAsync(configure);

    /// <summary>
    /// Stops and disposes the global <see cref="ProtoHost"/> asynchronously and cleans up assembly-wide resources.
    /// Call this inside your assembly-cleanup method.
    /// </summary>
    protected static Task CleanupAsync() => StopAsync();
}
