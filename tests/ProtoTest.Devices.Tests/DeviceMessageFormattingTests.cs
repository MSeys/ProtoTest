namespace ProtoTest.Devices.Tests;

using System.Threading.Channels;
using ProtoTest.Core;

[TestFixture]
public sealed class DeviceMessageFormattingTests
{
    [Test]
    public void Formatters_ShouldConvertScaledHexAndUnixFields()
    {
        var reading = new PumpReading(255, 12.5m, DateTimeOffset.FromUnixTimeSeconds(1_790_000_000));

        var text = DeviceMessage.Format(reading);

        Assert.Multiple(() =>
        {
            Assert.That(text, Is.EqualTo("PMP,FF,1250,1790000000"));
            Assert.That(DeviceMessage.Parse<PumpReading>(text), Is.EqualTo(reading));
        });
    }

    [Test]
    public void Formatter_ShouldNameAFieldItCannotRead()
    {
        var exception = Assert.Throws<DeviceMessageFormatException>(() => DeviceMessage.Parse<PumpReading>("PMP,ZZ,1250,1"));

        Assert.That(exception!.Message, Does.Contain("field 0 (Pump) is 'ZZ'").And.Contain("for Hex"));
    }

    [Test]
    public void Formatter_ForAnotherType_ShouldFailWhenTheLayoutIsBuilt()
        => Assert.That(
            () => DeviceMessage.Format(new Mismatched(1)),
            Throws.InvalidOperationException.With.Message.Contains("Mismatched.Volume is a Int32, and its formatter Hundredths converts a Decimal"));

    [Test]
    public void Checksum_OfTheWrongKind_ShouldFailWhenTheLayoutIsBuilt()
        => Assert.That(
            () => DeviceMessage.Format(new WrongChecksum("x")),
            Throws.InvalidOperationException.With.Message.Contains("is text, and its checksum Crc16Modbus is a binary checksum"));

    [Test]
    public void Crc16Modbus_ShouldMatchAPublishedFrame()
    {
        // Read holding registers: slave 1, function 3, address 0, count 1 -> CRC 84 0A.
        var crc = new byte[2];
        new Crc16Modbus().Compute(new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x01 }, crc);

        Assert.That(crc, Is.EqualTo(new byte[] { 0x84, 0x0A }));
    }

    [Test]
    public void BinaryMessage_ShouldWriteThePublishedModbusRequest()
    {
        var bytes = DeviceMessage.ToBytes(new ReadHoldingRegisters(Address: 0, Count: 1));

        Assert.Multiple(() =>
        {
            Assert.That(Convert.ToHexString(bytes), Is.EqualTo("010300000001840A"));
            Assert.That(DeviceMessage.FromBytes<ReadHoldingRegisters>(bytes), Is.EqualTo(new ReadHoldingRegisters(0, 1)));
        });
    }

    [Test]
    public void BinaryMessage_ShouldRoundTripFormattersStringsAndEnums()
    {
        var status = new MeterStatus(MeterMode.Running, 12.34m, "M-1", 123456, DateTimeOffset.FromUnixTimeSeconds(1_790_000_000), true);

        var bytes = DeviceMessage.ToBytes(status);

        Assert.Multiple(() =>
        {
            Assert.That(bytes, Has.Length.EqualTo(1 + 1 + 2 + 6 + 3 + 4 + 1 + 1), "prefix, enum, scaled int16, string, BCD, Unix seconds, bool, XOR");
            Assert.That(Convert.ToHexString(bytes.AsSpan(10, 3)), Is.EqualTo("123456"), "packed BCD");
            Assert.That(DeviceMessage.FromBytes<MeterStatus>(bytes), Is.EqualTo(status));
        });
    }

    [Test]
    public void BinaryMessage_ShouldNameAChecksumMismatchAndAWrongLength()
    {
        var bytes = DeviceMessage.ToBytes(new ReadHoldingRegisters(0, 1));
        bytes[^1] ^= 0xFF;

        Assert.Multiple(() =>
        {
            Assert.That(
                () => DeviceMessage.FromBytes<ReadHoldingRegisters>(bytes),
                Throws.TypeOf<DeviceMessageFormatException>().With.Message.Contains("its checksum is 84F5, but the body computes to 840A"));
            Assert.That(
                () => DeviceMessage.FromBytes<ReadHoldingRegisters>(bytes.AsSpan(0, 5)),
                Throws.TypeOf<DeviceMessageFormatException>().With.Message.Contains("it is 5 bytes, and ReadHoldingRegisters is 8"));
        });
    }

    [Test]
    public void Frames_ShouldCarryTextAndBinaryMessages()
    {
        var text = DeviceMessage.ToFrame(new PumpReading(1, 1m, DateTimeOffset.UnixEpoch));
        var binary = DeviceMessage.ToFrame(new ReadHoldingRegisters(0, 1));

        Assert.Multiple(() =>
        {
            Assert.That(text.MediaType, Is.EqualTo("text/plain"));
            Assert.That(binary.MediaType, Is.EqualTo("application/octet-stream"));
            Assert.That(DeviceMessage.TryRead<ReadHoldingRegisters>(binary, out var request), Is.True);
            Assert.That(request, Is.EqualTo(new ReadHoldingRegisters(0, 1)));
            Assert.That(DeviceMessage.TryRead<ReadHoldingRegisters>(text, out _), Is.False);
        });
    }

    [Test]
    public async Task MessageProtocol_ShouldCountExpectedTypesAndReportTheRestAsGaps()
    {
        var protocol = new DeviceMessageProtocol("Pump protocol").Add<PumpReading>().Add<PumpAlarm>();
        var collector = new DeviceCoverageCollector("Pumps", [protocol]);
        var transport = new RepliesTransport(DeviceMessage.ToFrame(new PumpReading(1, 2m, DateTimeOffset.UnixEpoch)));
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureServices(services => Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<IProtoCollector>(services, collector));
        builder.AddDevices(devices => devices
            .AddClient("Pumps", transport, resolveAddress: ProtoDeviceAddress.Template("memory://{deviceId}"))
            .AddProtocol(protocol)
            .AddDevice<Pump>());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("message coverage", "00001", TestMethods.Placeholder);

        await Proto.Context.Devices("Pumps").For<Pump>("P-1").ReadAsync();

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
        var items = collector.GetReportItems().ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(items.Single(item => item.Identifier == nameof(PumpReading)).IsCovered, Is.True);
            Assert.That(items.Single(item => item.Identifier == nameof(PumpAlarm)).IsCovered, Is.False, "no test expected an alarm");
        });
    }

    [DeviceMessage("PMP")]
    private sealed record PumpReading(
        [DeviceFormat<DeviceFormatters.Hex>] int Pump,
        [DeviceFormat<DeviceFormatters.Hundredths>] decimal Flow,
        [DeviceFormat<DeviceFormatters.UnixSeconds>] DateTimeOffset At);

    [DeviceMessage("ALM")]
    private sealed record PumpAlarm(int Pump, string Code);

    private sealed record Mismatched([DeviceFormat<DeviceFormatters.Hundredths>] int Volume);

    [DeviceChecksum<Crc16Modbus>]
    private sealed record WrongChecksum(string Value);

    [DeviceBinaryMessage(0x01, 0x03)]
    [DeviceChecksum<Crc16Modbus>]
    private sealed record ReadHoldingRegisters(ushort Address, ushort Count);

    private enum MeterMode : byte
    {
        Idle = 0,
        Running = 1
    }

    private sealed class Centi() : DeviceFormatters.ScaledInt16(2);

    private sealed class SixDigits() : DeviceFormatters.Bcd(3);

    [DeviceBinaryMessage(0xA5)]
    [DeviceChecksum<Xor8Checksum>]
    private sealed record MeterStatus(
        MeterMode Mode,
        [DeviceFormat<Centi>] decimal Flow,
        [DeviceField(Bytes = 6)] string Id,
        [DeviceFormat<SixDigits>] long Total,
        [DeviceFormat<DeviceFormatters.BinaryUnixSeconds>] DateTimeOffset At,
        bool Alarm);

    private sealed class Pump : ProtoDevice
    {
        public async Task<PumpReading> ReadAsync()
        {
            await SendTextAsync("READ");
            return await ExpectMessageAsync<PumpReading>();
        }
    }

    private sealed class RepliesTransport(params DeviceFrame[] replies) : IProtoDeviceTransport
    {
        public string Name => "Replies";

        public ValueTask<IProtoDeviceConnection> ConnectAsync(DeviceEndpoint endpoint, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IProtoDeviceConnection>(new Connection(replies));

        private sealed class Connection : IProtoDeviceConnection
        {
            private readonly Channel<DeviceFrame> _incoming = Channel.CreateUnbounded<DeviceFrame>();

            public Connection(DeviceFrame[] replies)
            {
                foreach (var reply in replies)
                {
                    _incoming.Writer.TryWrite(reply);
                }
            }

            public string? RemoteAddress => "memory://";

            public ValueTask SendAsync(DeviceFrame frame, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

            public async ValueTask<DeviceFrame?> ReceiveAsync(CancellationToken cancellationToken = default)
                => await _incoming.Reader.ReadAsync(cancellationToken);

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
