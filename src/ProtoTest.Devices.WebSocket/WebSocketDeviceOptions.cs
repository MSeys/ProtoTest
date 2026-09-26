namespace ProtoTest.Devices.WebSocket;

using ProtoTest.Core;

/// <summary>
/// How the WebSocket transport connects and reads. Code sets defaults through the client registration
/// (<c>AddWebSocketClient(..., configure)</c>) or <c>AddInProcessWebSocketDevices(configure)</c>;
/// <c>ProtoTest:Devices:WebSocket</c> overrides them per environment.
/// </summary>
public sealed class WebSocketDeviceOptions : IProtoConfigurableOptions
{
    /// <summary>The configuration section this type binds from.</summary>
    public const string ConfigurationSectionName = "ProtoTest:Devices:WebSocket";

    string IProtoConfigurableOptions.ConfigurationSectionName => ConfigurationSectionName;

    /// <summary>Gets or sets how long a connection attempt may take. Defaults to 10 seconds.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Gets or sets the buffer a receive reads into. Defaults to 16 KB.</summary>
    public int ReceiveBufferBytes { get; set; } = 16 * 1024;

    /// <summary>
    /// Gets or sets the largest message a receive assembles from fragments. Defaults to 4 MiB; a
    /// larger message fails with <see cref="DeviceFrameTooLargeException"/> instead of accumulating
    /// until the process runs out of memory. Set to zero to read without a cap.
    /// </summary>
    public long MaxMessageBytes { get; set; } = 4 * 1024 * 1024;

    /// <summary>Gets or sets the keep-alive interval; <see langword="null"/> leaves the default.</summary>
    public TimeSpan? KeepAliveInterval { get; set; }

    /// <inheritdoc />
    public void Validate()
    {
        if (ConnectTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ConnectTimeout), ConnectTimeout, "The connect timeout must be positive.");
        }

        if (ReceiveBufferBytes < 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(ReceiveBufferBytes), ReceiveBufferBytes, "The receive buffer must be at least 1 KB.");
        }

        if (MaxMessageBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxMessageBytes), MaxMessageBytes, "The maximum message size cannot be negative.");
        }
    }
}
