namespace ProtoTest.Devices.Tcp;

using ProtoTest.Core;

/// <summary>
/// How the TCP transports connect, listen and read. Code sets defaults through the client registration
/// (<c>AddTcpClient(..., configure)</c> or <c>AddTcpListener(..., configure)</c>);
/// <c>ProtoTest:Devices:Tcp</c> overrides them per environment. One set applies to the whole run.
/// </summary>
public sealed class TcpDeviceOptions : IProtoConfigurableOptions
{
    /// <summary>The configuration section this type binds from.</summary>
    public const string ConfigurationSectionName = "ProtoTest:Devices:Tcp";

    string IProtoConfigurableOptions.ConfigurationSectionName => ConfigurationSectionName;

    /// <summary>Gets or sets how long a connection attempt may take. Defaults to 10 seconds.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets how long a listening device waits for the system under test to connect, counted from
    /// its first send or receive. Defaults to 30 seconds.
    /// </summary>
    public TimeSpan AcceptTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the most bytes a receive buffers without completing a frame. Defaults to 1 MiB; more
    /// fails with a framing error that shows the first bytes, which usually means the framer is wrong.
    /// </summary>
    public int MaxFrameBytes { get; set; } = StreamDeviceConnection.DefaultMaxFrameBytes;

    /// <summary>Gets or sets whether small writes go out at once instead of waiting to batch. Defaults to true.</summary>
    public bool NoDelay { get; set; } = true;

    /// <inheritdoc />
    public void Validate()
    {
        if (ConnectTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ConnectTimeout), ConnectTimeout, "The connect timeout must be positive.");
        }

        if (AcceptTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(AcceptTimeout), AcceptTimeout, "The accept timeout must be positive.");
        }

        if (MaxFrameBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxFrameBytes), MaxFrameBytes, "The frame limit must be positive.");
        }
    }
}
