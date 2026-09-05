namespace ProtoTest.MSTest;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProtoTest.Core;

/// <summary>
/// Abstract base class for MSTest assembly configuration.
/// Inherit from this class in your test project, add <see cref="TestClassAttribute"/>, 
/// and wrap your initialization logic inside <see cref="AssemblyInitializeAttribute"/>.
/// </summary>
public abstract class ProtoTestAssembly
{
    private static ProtoHost? _host;

    /// <summary>
    /// Gets the current initialized <see cref="ProtoHost"/> instance.
    /// </summary>
    public static ProtoHost Host => _host
        ?? throw new InvalidOperationException("ProtoHost is not initialized. Ensure your setup class inherits from ProtoTestAssembly and calls Initialize().");

    /// <summary>
    /// Initializes the global <see cref="ProtoHost"/> for the test assembly.
    /// Call this inside your <see cref="AssemblyInitializeAttribute"/> method.
    /// </summary>
    /// <param name="configure">Delegate to configure services and hooks via <see cref="IProtoHostBuilder"/>.</param>
    protected static void Initialize(Action<IProtoHostBuilder> configure)
    {
        var builder = new ProtoHostBuilder();
        configure(builder);
        _host = builder.Build();
    }

    /// <summary>
    /// Disposes the global <see cref="ProtoHost"/> and cleans up assembly-wide resources.
    /// Call this inside your <see cref="AssemblyCleanupAttribute"/> method.
    /// </summary>
    protected static void Cleanup()
    {
        _host?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _host = null;
    }
}