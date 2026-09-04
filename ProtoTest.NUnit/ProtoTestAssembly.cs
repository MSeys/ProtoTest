namespace ProtoTest.NUnit;

using global::NUnit.Framework;
using ProtoTest.Core;

/// <summary>
/// Base class for global assembly setup in NUnit.
/// Initializes and disposes the root <see cref="ProtoHost"/>.
/// </summary>
[SetUpFixture]
public abstract class ProtoTestAssembly
{
    private static ProtoHost? _host;

    /// <summary>
    /// Gets the global <see cref="ProtoHost"/> instance.
    /// </summary>
    public static ProtoHost Host => _host
        ?? throw new InvalidOperationException("ProtoHost is not initialized. Ensure your setup class inherits from ProtoTestAssembly.");

    [OneTimeSetUp]
    public void GlobalSetUp()
    {
        var builder = new ProtoHostBuilder();
        Configure(builder);
        _host = builder.Build();
    }

    [OneTimeTearDown]
    public async Task GlobalTearDown()
    {
        if (_host != null)
        {
            await _host.DisposeAsync();
        }
    }

    /// <summary>
    /// Configures the <see cref="IProtoHostBuilder"/> with custom services and hooks.
    /// </summary>
    protected abstract void Configure(IProtoHostBuilder builder);
}