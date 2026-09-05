namespace ProtoTest.Xunit3;

using global::Xunit;
using ProtoTest.Core;

/// <summary>
/// Base assembly fixture for xUnit v3. Manages global <see cref="ProtoHost"/> lifecycle.
/// </summary>
public abstract class ProtoTestAssembly : IAsyncLifetime
{
    private static ProtoHost? _host;

    public static ProtoHost Host => _host
        ?? throw new InvalidOperationException("ProtoHost is not initialized. Register your fixture with [assembly: AssemblyFixture(...)].");

    public ValueTask InitializeAsync()
    {
        var builder = new ProtoHostBuilder();
        Configure(builder);
        _host = builder.Build();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_host != null)
        {
            await _host.DisposeAsync();
        }
    }

    protected abstract void Configure(IProtoHostBuilder builder);
}