namespace ProtoTest.Devices.Mqtt;

using ProtoTest.Core;

/// <summary>
/// How the MQTT transport reaches its broker and moves frames. Code sets defaults through
/// <c>AddMqttClient(..., configure)</c>; <c>ProtoTest:Devices:Mqtt</c> overrides them per environment.
/// </summary>
public sealed class MqttDeviceOptions : IProtoConfigurableOptions
{
    /// <summary>The configuration section this type binds from.</summary>
    public const string ConfigurationSectionName = "ProtoTest:Devices:Mqtt";

    /// <summary>
    /// The key a broker container or a configured environment fills with the broker address; a client
    /// registered without an address resolves through it.
    /// </summary>
    public const string BrokerSetting = ConfigurationSectionName + ":Broker";

    string IProtoConfigurableOptions.ConfigurationSectionName => ConfigurationSectionName;

    /// <summary>
    /// Gets or sets the broker address (<c>mqtt://host:port</c>) a client registered without an
    /// address or a resolver uses. A started piece that publishes <see cref="BrokerSetting"/> wins
    /// over it, and an explicit address or resolver on the registration wins over both.
    /// </summary>
    public string? Broker { get; set; }

    /// <summary>Gets or sets how long a connection attempt may take. Defaults to 10 seconds.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Gets or sets the keep-alive interval sent to the broker; <see langword="null"/> leaves the library default.</summary>
    public TimeSpan? KeepAlivePeriod { get; set; }

    /// <summary>
    /// Gets or sets the largest MQTT packet the client accepts, offered to the broker as MQTT 5
    /// <c>MaximumPacketSize</c>. Defaults to 4 MiB; set to zero to read without a cap.
    /// </summary>
    public long MaxPacketBytes { get; set; } = 4 * 1024 * 1024;

    /// <inheritdoc />
    public void Validate()
    {
        if (!string.IsNullOrWhiteSpace(Broker) && !IsMqttAddress(Broker))
        {
            throw new ArgumentException(
                $"MqttDeviceOptions.Broker must be an absolute mqtt:// address naming the broker, not '{Broker}'. " +
                $"Set it in code or under '{BrokerSetting}'.",
                nameof(Broker));
        }

        if (ConnectTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ConnectTimeout), ConnectTimeout, "The connect timeout must be positive.");
        }

        if (KeepAlivePeriod is { } keepAlive && keepAlive <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(KeepAlivePeriod), keepAlive, "The keep-alive interval must be positive.");
        }

        if (MaxPacketBytes < 0 || MaxPacketBytes > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxPacketBytes),
                MaxPacketBytes,
                $"The maximum packet size must be between 0 and {uint.MaxValue} bytes.");
        }
    }

    /// <summary>A broker address the MQTT transport can open: plain <c>mqtt://</c> over TCP.</summary>
    internal static bool IsMqttAddress(string address)
        => Uri.TryCreate(address, UriKind.Absolute, out var uri)
            && string.Equals(uri.Scheme, "mqtt", StringComparison.OrdinalIgnoreCase);
}
