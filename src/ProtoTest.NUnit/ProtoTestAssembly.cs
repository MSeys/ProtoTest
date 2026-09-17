namespace ProtoTest.NUnit;

using global::NUnit.Framework;
using ProtoTest.Core;

/// <summary>
/// Base class for global assembly setup in NUnit.
/// Initializes, starts, and disposes the root <see cref="ProtoHost"/>.
/// </summary>
[SetUpFixture]
public abstract class ProtoTestAssembly
{
    private static readonly ProtoTestHostLifetime Lifetime = new(
        "Ensure your setup class inherits from ProtoTestAssembly.");

    /// <summary>
    /// Gets the global <see cref="ProtoHost"/> instance.
    /// </summary>
    public static ProtoHost Host => Lifetime.Host;

    [OneTimeSetUp]
    public Task GlobalSetUp() => Lifetime.StartAsync(Configure);

    [OneTimeTearDown]
    public Task GlobalTearDown() => Lifetime.StopAsync();

    /// <summary>
    /// Configures the <see cref="IProtoHostBuilder"/> with custom services, collectors, and hooks.
    /// </summary>
    protected abstract void Configure(IProtoHostBuilder builder);
}
