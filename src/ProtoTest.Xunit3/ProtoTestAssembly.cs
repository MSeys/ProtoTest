namespace ProtoTest.Xunit3;

using Xunit;
using ProtoTest.Core;

/// <summary>
/// Base assembly fixture for xUnit v3.
/// Initializes, starts, and disposes the root <see cref="ProtoHost"/>.
/// </summary>
public abstract class ProtoTestAssembly
    : ProtoTestAssemblyHost<ProtoTestAssembly>, IProtoTestAssemblyHost<ProtoTestAssembly>, IAsyncLifetime
{
    static string IProtoTestAssemblyHost<ProtoTestAssembly>.UninitializedHint =>
        "Register your fixture with [assembly: AssemblyFixture(...)].";

    /// <inheritdoc />
    public ValueTask InitializeAsync() => new(StartAsync(Configure));

    /// <inheritdoc />
    public ValueTask DisposeAsync() => new(StopAsync());

    /// <summary>
    /// Configures the <see cref="IProtoHostBuilder"/> with custom services, collectors, and hooks.
    /// </summary>
    /// <param name="builder">The host builder instance.</param>
    protected abstract void Configure(IProtoHostBuilder builder);
}
