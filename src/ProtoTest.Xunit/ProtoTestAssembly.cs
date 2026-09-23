namespace ProtoTest.Xunit;

using global::Xunit;
using ProtoTest.Core;

/// <summary>
/// Base fixture for global assembly setup in xUnit v2.
/// Initializes, starts, and disposes the root <see cref="ProtoHost"/>.
/// </summary>
public abstract class ProtoTestAssembly
    : ProtoTestAssemblyHost<ProtoTestAssembly>, IProtoTestAssemblyHost<ProtoTestAssembly>, IAsyncLifetime
{
    static string IProtoTestAssemblyHost<ProtoTestAssembly>.UninitializedHint =>
        "Ensure your collection fixture inherits from ProtoTestAssembly.";

    /// <inheritdoc />
    public Task InitializeAsync() => StartAsync(Configure);

    /// <inheritdoc />
    public Task DisposeAsync() => StopAsync();

    /// <summary>
    /// Configures the <see cref="IProtoHostBuilder"/> with custom services, collectors, and hooks.
    /// </summary>
    /// <param name="builder">The host builder instance.</param>
    protected abstract void Configure(IProtoHostBuilder builder);
}
