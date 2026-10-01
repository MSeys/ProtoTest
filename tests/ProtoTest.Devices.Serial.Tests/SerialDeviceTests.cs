namespace ProtoTest.Devices.Serial.Tests;

using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Win32.SafeHandles;
using ProtoTest.Core;

[TestFixture]
public sealed class SerialDeviceTests
{
    [Test]
    public void Address_ShouldDefaultTo9600EightNoneOne()
    {
        var line = SerialPortAddress.Parse("serial://COM3");

        Assert.That(line, Is.EqualTo(new SerialPortAddress("COM3", 9600, Parity.None, 8, StopBits.One, Handshake.None)));
    }

    [Test]
    public void Address_ShouldReadEverySetting()
    {
        var line = SerialPortAddress.Parse("serial:///dev/ttyUSB0?baud=115200&databits=7&parity=even&stopbits=2&handshake=RequestToSend");

        Assert.That(line, Is.EqualTo(new SerialPortAddress("/dev/ttyUSB0", 115200, Parity.Even, 7, StopBits.Two, Handshake.RequestToSend)));
    }

    [TestCase("tcp://COM3", "is not a serial device address")]
    [TestCase("serial://", "names no serial port")]
    [TestCase("serial://COM3?speed=9600", "has the setting 'speed'")]
    [TestCase("serial://COM3?parity=odd2", "use one of None, Odd, Even, Mark, Space")]
    [TestCase("serial://COM3?baud=fast", "baud is a positive whole number")]
    public void Address_ShouldNameWhatItCannotRead(string address, string reason)
        => Assert.That(() => SerialPortAddress.Parse(address), Throws.InvalidOperationException.With.Message.Contains(reason));

    [Test]
    public async Task SerialClient_WithoutAPortKey_ShouldDropTheDeviceCapability()
    {
        var builder = new ProtoHostBuilder();
        builder.AddDevices(devices => devices.AddSerialClient("Meters").AddDevice<Meter>());
        await using var host = builder.Build();
        await host.StartAsync();
        var skipped = host.Trace.Snapshot().Entries!.Single(entry =>
            entry.Kind == "capability.skipped"
            && entry.Attributes["capability.name"] == nameof(Meter));
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(host.HasCapability(ProtoCapabilityKinds.Device, nameof(Meter)), Is.False, "[RequiresDevice] skips on a machine without the port");
            Assert.That(skipped.Attributes["capability.keys"], Does.Contain("ProtoTest:Devices:Serial:Ports:Meters"));
        });
    }

    [Test]
    public async Task SerialClient_WhenThePortIsMissing_ShouldNameItAndTheMachinesPorts()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ProtoTest:Devices:Serial:Ports:Meters"] = "serial:///dev/protoTestNoSuchPort?baud=9600"
        }));
        builder.AddDevices(devices => devices.AddSerialClient("Meters").AddDevice<Meter>());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("serial missing", "00001", TestMethods.Placeholder);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Proto.Context.Devices("Meters").For<Meter>("M-1").PingAsync());

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.That(
            exception!.Message,
            Does.Contain("The serial port '/dev/protoTestNoSuchPort' could not be opened").And.Contain("Ports on this machine"));
    }

    [Test]
    [Platform("Linux")]
    public async Task SerialClient_ShouldExchangeFramesOverARealTerminalLine()
    {
        using var terminal = PseudoTerminal.Open();
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddDevices(devices => devices
            .AddSerialClient("Meters", DeviceFramers.Lines("\r\n"), address: $"serial://{terminal.PortName}?baud=115200")
            .AddDevice<Meter>());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("serial pty", "00001", TestMethods.Placeholder);
        var meter = Proto.Context.Devices("Meters").For<Meter>("M-1");

        var pinging = meter.PingAsync();
        var request = await terminal.ReadLineAsync();
        terminal.Write(DeviceMessage.Format(new MeterReading("M-1", 12.5m)) + "\r\n");
        var reading = await pinging;

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.Multiple(() =>
        {
            Assert.That(request, Is.EqualTo("PING M-1"));
            Assert.That(reading, Is.EqualTo(new MeterReading("M-1", 12.5m)));
        });
    }

    [DeviceMessage("$MTR", Checksum = typeof(NmeaChecksum))]
    private sealed record MeterReading(string MeterId, [DeviceField(Format = "0.00")] decimal Volume);

    private sealed class Meter : ProtoDevice
    {
        public async Task<MeterReading> PingAsync()
        {
            await SendTextAsync($"PING {Id}");
            return await ExpectMessageAsync<MeterReading>(reading => reading.MeterId == Id);
        }
    }

    /// <summary>A Linux pseudo-terminal: the device opens the line end as its serial port, the test holds the other.</summary>
    private sealed class PseudoTerminal : IDisposable
    {
        private const int ReadWrite = 2;
        private const int NoControllingTerminal = 0x100;
        private readonly FileStream _controller;

        private PseudoTerminal(FileStream controller, string portName)
        {
            _controller = controller;
            PortName = portName;
        }

        public string PortName { get; }

        public static PseudoTerminal Open()
        {
            var descriptor = posix_openpt(ReadWrite | NoControllingTerminal);
            if (descriptor < 0 || grantpt(descriptor) != 0 || unlockpt(descriptor) != 0)
            {
                Assert.Ignore("This machine cannot open a pseudo-terminal.");
            }

            var name = Marshal.PtrToStringAnsi(ptsname(descriptor))!;
            var handle = new SafeFileHandle(descriptor, ownsHandle: true);
            return new PseudoTerminal(new FileStream(handle, FileAccess.ReadWrite, bufferSize: 0), name);
        }

        public Task<string> ReadLineAsync() => Task.Run(() =>
        {
            var line = new StringBuilder();
            var buffer = new byte[1];
            while (_controller.Read(buffer, 0, 1) == 1)
            {
                if (buffer[0] == '\n')
                {
                    return line.ToString().TrimEnd('\r');
                }

                line.Append((char)buffer[0]);
            }

            return line.ToString();
        }).WaitAsync(TimeSpan.FromSeconds(10));

        public void Write(string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            _controller.Write(bytes, 0, bytes.Length);
            _controller.Flush();
        }

        public void Dispose() => _controller.Dispose();

        [DllImport("libc", SetLastError = true)]
        private static extern int posix_openpt(int flags);

        [DllImport("libc", SetLastError = true)]
        private static extern int grantpt(int descriptor);

        [DllImport("libc", SetLastError = true)]
        private static extern int unlockpt(int descriptor);

        [DllImport("libc", SetLastError = true)]
        private static extern IntPtr ptsname(int descriptor);
    }
}
