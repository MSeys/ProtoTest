namespace ProtoTest.TUnit;

using ProtoTest.Core;

/// <summary>
/// Abstract base class for TUnit assembly configuration.
/// Inherit from this class in your test project to configure global services and hooks.
/// </summary>
public abstract class ProtoTestAssembly
{
    private static ProtoHost? _host;

    public static ProtoHost Host => _host
        ?? throw new InvalidOperationException("ProtoHost is not initialized. Ensure your setup class inherits from ProtoTestAssembly.");

    protected static void Initialize(Action<IProtoHostBuilder> configure)
    {
        if (_host != null)
        {
            throw new InvalidOperationException("ProtoHost has already been initialized for this assembly.");
        }

        var builder = new ProtoHostBuilder();
        configure(builder);
        _host = builder.Build();
    }

    protected static async Task CleanupAsync()
    {
        if (_host != null)
        {
            await _host.DisposeAsync();
            _host = null;
        }
    }
}