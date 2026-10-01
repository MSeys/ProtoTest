namespace ProtoTest.Devices.Serial;

using System.Globalization;
using System.IO.Ports;
using ProtoTest.Core;
using ProtoTest.Devices;

/// <summary>
/// How the serial transport reads. Code sets defaults through <c>AddSerialClient(..., configure)</c>;
/// <c>ProtoTest:Devices:Serial</c> overrides them per environment. The line settings (baud rate, parity)
/// belong to each port's address.
/// </summary>
public sealed class SerialDeviceOptions : IProtoConfigurableOptions
{
    /// <summary>The configuration section this type binds from.</summary>
    public const string ConfigurationSectionName = "ProtoTest:Devices:Serial";

    /// <summary>The section a client without an address reads its port from, as <c>Ports:{client}</c>.</summary>
    public const string PortsSection = ConfigurationSectionName + ":Ports";

    string IProtoConfigurableOptions.ConfigurationSectionName => ConfigurationSectionName;

    /// <summary>
    /// Gets or sets the most bytes a receive buffers without completing a frame. Defaults to 1 MiB; more
    /// fails with a framing error that shows the first bytes.
    /// </summary>
    public int MaxFrameBytes { get; set; } = StreamDeviceConnection.DefaultMaxFrameBytes;

    /// <inheritdoc />
    public void Validate()
    {
        if (MaxFrameBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxFrameBytes), MaxFrameBytes, "The frame limit must be positive.");
        }
    }
}

/// <summary>
/// A serial line: the port and its settings, read from <c>serial://COM3?baud=9600</c> or
/// <c>serial:///dev/ttyUSB0?baud=115200&amp;parity=even</c>. Unset settings default to 9600 baud, 8 data
/// bits, no parity, one stop bit and no handshake.
/// </summary>
public sealed record SerialPortAddress(
    string PortName,
    int BaudRate = 9600,
    Parity Parity = Parity.None,
    int DataBits = 8,
    StopBits StopBits = StopBits.One,
    Handshake Handshake = Handshake.None)
{
    /// <summary>Reads a <c>serial://</c> address, or fails naming the part it could not read.</summary>
    public static SerialPortAddress Parse(string address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        const string scheme = "serial://";
        if (!address.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"'{address}' is not a serial device address; use serial://COM3?baud=9600 or serial:///dev/ttyUSB0?baud=9600.");
        }

        var rest = address[scheme.Length..];
        var query = rest.IndexOf('?', StringComparison.Ordinal);
        var port = query < 0 ? rest : rest[..query];
        if (string.IsNullOrWhiteSpace(port))
        {
            throw new InvalidOperationException($"'{address}' names no serial port.");
        }

        var result = new SerialPortAddress(Uri.UnescapeDataString(port));
        if (query < 0)
        {
            return result;
        }

        foreach (var pair in rest[(query + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            var key = equals < 0 ? pair : pair[..equals];
            var value = equals < 0 ? string.Empty : Uri.UnescapeDataString(pair[(equals + 1)..]);
            result = key.ToLowerInvariant() switch
            {
                "baud" => result with { BaudRate = Number(address, key, value) },
                "databits" => result with { DataBits = Number(address, key, value) },
                "parity" => result with { Parity = Named<Parity>(address, key, value) },
                "stopbits" => result with { StopBits = StopBitsFrom(address, key, value) },
                "handshake" => result with { Handshake = Named<Handshake>(address, key, value) },
                _ => throw new InvalidOperationException(
                    $"'{address}' has the setting '{key}'; a serial address knows baud, databits, parity, stopbits and handshake.")
            };
        }

        return result;
    }

    /// <summary>Formats the address back, with every setting spelled out.</summary>
    public override string ToString()
        => string.Create(
            CultureInfo.InvariantCulture,
            $"serial://{PortName}?baud={BaudRate}&databits={DataBits}&parity={Parity}&stopbits={StopBits}&handshake={Handshake}");

    private static int Number(string address, string key, string value)
        => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0
            ? number
            : throw new InvalidOperationException($"'{address}' has {key}={value}; {key} is a positive whole number.");

    private static StopBits StopBitsFrom(string address, string key, string value) => value switch
    {
        "1" => StopBits.One,
        "1.5" => StopBits.OnePointFive,
        "2" => StopBits.Two,
        _ => Named<StopBits>(address, key, value)
    };

    private static TEnum Named<TEnum>(string address, string key, string value)
        where TEnum : struct, Enum
        => Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) && !int.TryParse(value, out _)
            ? parsed
            : throw new InvalidOperationException(
                $"'{address}' has {key}={value}; use one of {string.Join(", ", Enum.GetNames<TEnum>())}.");
}

/// <summary>
/// Talks to a device on a serial line. The client's framer cuts the line's bytes into frames; a client
/// without one reads newline-terminated text. The port opens on the device's first use and closes with
/// the test; a port is open in one test at a time, so tests that share a port must not run in parallel.
/// </summary>
public sealed class SerialDeviceTransport : IProtoDeviceTransport
{
    /// <summary>The transport's name, as registrations and the trace refer to it.</summary>
    public const string TransportName = "Serial";

    private readonly SerialDeviceOptions _options;

    /// <summary>Creates the transport with the run's serial options.</summary>
    public SerialDeviceTransport(SerialDeviceOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public string Name => TransportName;

    /// <inheritdoc />
    public ValueTask<IProtoDeviceConnection> ConnectAsync(
        DeviceEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        cancellationToken.ThrowIfCancellationRequested();
        var line = SerialPortAddress.Parse(endpoint.Address);
        var port = new SerialPort(line.PortName, line.BaudRate, line.Parity, line.DataBits, line.StopBits)
        {
            Handshake = line.Handshake
        };
        try
        {
            port.Open();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            port.Dispose();
            throw new InvalidOperationException(
                $"The serial port '{line.PortName}' could not be opened: {exception.Message} " +
                $"Ports on this machine: {Describe(SerialPort.GetPortNames())}.",
                exception);
        }

        return ValueTask.FromResult<IProtoDeviceConnection>(new StreamDeviceConnection(
            port.BaseStream,
            endpoint.Framer ?? DeviceFramers.Lines(),
            line.ToString(),
            _options.MaxFrameBytes,
            new PortOwner(port)));
    }

    private static string Describe(string[] ports) => ports.Length == 0 ? "none" : string.Join(", ", ports.Order(StringComparer.Ordinal));

    private sealed class PortOwner(SerialPort port) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            port.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
