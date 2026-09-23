namespace ProtoTest.NUnit;

using global::NUnit.Framework;
using ProtoTest.Core;

/// <summary>
/// Base class for global assembly setup in NUnit.
/// Initializes, starts, and disposes the root <see cref="ProtoHost"/>.
/// </summary>
[SetUpFixture]
public abstract class ProtoTestAssembly
    : ProtoTestAssemblyHost<ProtoTestAssembly>, IProtoTestAssemblyHost<ProtoTestAssembly>
{
    static string IProtoTestAssemblyHost<ProtoTestAssembly>.UninitializedHint =>
        "Ensure your setup class inherits from ProtoTestAssembly.";

    [OneTimeSetUp]
    public Task GlobalSetUp() => StartAsync(Configure);

    [OneTimeTearDown]
    public Task GlobalTearDown() => StopAsync();

    /// <summary>
    /// Configures the <see cref="IProtoHostBuilder"/> with custom services, collectors, and hooks.
    /// </summary>
    /// <param name="builder">The host builder instance.</param>
    protected abstract void Configure(IProtoHostBuilder builder);
}
