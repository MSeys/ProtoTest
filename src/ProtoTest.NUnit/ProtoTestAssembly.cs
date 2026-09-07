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
    private static ProtoHost? _host;

    /// <summary>
    /// Gets the global <see cref="ProtoHost"/> instance.
    /// </summary>
    public static ProtoHost Host => _host
        ?? throw new InvalidOperationException("ProtoHost is not initialized. Ensure your setup class inherits from ProtoTestAssembly.");

    [OneTimeSetUp]
    public async Task GlobalSetUp()
    {
        var builder = new ProtoHostBuilder();
        Configure(builder);

        _host = builder.Build();

        // Trigger suite-level before run hooks (e.g. downloading OpenAPI specs, starting test environments)
        await _host.StartAsync();
    }

    [OneTimeTearDown]
    public async Task GlobalTearDown()
    {
        if (_host != null)
        {
            try
            {
                // Trigger suite-level after run hooks (e.g. generating coverage reports)
                await _host.StopAsync();
            }
            finally
            {
                await _host.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Configures the <see cref="IProtoHostBuilder"/> with custom services, collectors, and hooks.
    /// </summary>
    protected abstract void Configure(IProtoHostBuilder builder);
}