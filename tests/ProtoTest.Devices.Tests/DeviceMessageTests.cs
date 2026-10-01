namespace ProtoTest.Devices.Tests;

using System.Threading.Channels;
using ProtoTest.Core;
using ProtoTest.Devices.Exceptions;

[TestFixture]
public sealed class DeviceMessageTests
{
    [Test]
    public void NmeaChecksum_ShouldMatchAPublishedSentence()
        => Assert.That(
            new NmeaChecksum().Compute("$GPGGA,123519,4807.038,N,01131.000,E,1,08,0.9,545.4,M,46.9,M,,"),
            Is.EqualTo("47"));

    [Test]
    public void Format_ShouldWriteThePrefixFieldsAndChecksum()
    {
        var text = DeviceMessage.Format(new MeterReading("M-1", 12.5m, new DateTimeOffset(2026, 10, 1, 9, 30, 0, TimeSpan.Zero)));

        Assert.That(text, Is.EqualTo("$MTR,M-1,12.50,20261001093000*" + new NmeaChecksum().Compute("$MTR,M-1,12.50,20261001093000")));
    }

    [Test]
    public void Parse_ShouldReadBackWhatFormatWrote()
    {
        var reading = new MeterReading("M-1", 12.5m, new DateTimeOffset(2026, 10, 1, 9, 30, 0, TimeSpan.Zero));

        Assert.That(DeviceMessage.Parse<MeterReading>(DeviceMessage.Format(reading)), Is.EqualTo(reading));
    }

    [TestCase("$MTR,M-1,12.50,20261001093000*00", "its checksum is 00")]
    [TestCase("$XYZ,M-1,12.50,20261001093000", "has no '*' before a checksum")]
    [TestCase("$MTR,M-1,abc,20261001093000", "field 1 (Volume) is 'abc', which is not a Decimal")]
    [TestCase("$MTR,M-1,12.50", "it has 2 fields, and MeterReading has 3")]
    public void Parse_ShouldNameWhatDoesNotMatch(string body, string reason)
    {
        var text = body.Contains('*', StringComparison.Ordinal) || !body.StartsWith("$MTR", StringComparison.Ordinal)
            ? body
            : body + "*" + new NmeaChecksum().Compute(body);

        var exception = Assert.Throws<DeviceMessageFormatException>(() => DeviceMessage.Parse<MeterReading>(text));

        Assert.That(exception!.Message, Does.Contain("is not a MeterReading").And.Contain(reason));
    }

    [Test]
    public void FixedWidth_ShouldPadAndTrimEachField()
    {
        var status = new PumpStatus { Pump = 7, State = PumpState.Run, Flow = 42 };

        var text = DeviceMessage.Format(status);
        var parsed = DeviceMessage.Parse<PumpStatus>(text);

        Assert.Multiple(() =>
        {
            Assert.That(text, Is.EqualTo("P07Run   0042"));
            Assert.That(parsed.Pump, Is.EqualTo(7));
            Assert.That(parsed.State, Is.EqualTo(PumpState.Run));
            Assert.That(parsed.Flow, Is.EqualTo(42));
        });
    }

    [Test]
    public void Fields_ShouldMapEmptyToNullAndBooleansToTheirWords()
    {
        var alarm = new Alarm("A-1", Active: true, Code: null);

        var text = DeviceMessage.Format(alarm);

        Assert.Multiple(() =>
        {
            Assert.That(text, Is.EqualTo("A-1;Y;"));
            Assert.That(DeviceMessage.Parse<Alarm>(text), Is.EqualTo(alarm));
            Assert.That(DeviceMessage.Parse<Alarm>("A-1;N;17"), Is.EqualTo(new Alarm("A-1", false, 17)));
        });
    }

    [Test]
    public void Format_ShouldRefuseAValueThatContainsTheSeparator()
        => Assert.That(
            () => DeviceMessage.Format(new MeterReading("M,1", 1m, DateTimeOffset.UnixEpoch)),
            Throws.TypeOf<DeviceMessageFormatException>().With.Message.Contains("MeterReading.MeterId is 'M,1', which contains the separator"));

    [Test]
    public void TryParse_ShouldReturnFalseForAnotherMessage()
        => Assert.That(DeviceMessage.TryParse<MeterReading>("$GPGGA,1,2", out _), Is.False);

    [Test]
    public void Layout_ShouldNameAClassWithoutFieldOrder()
        => Assert.That(
            () => DeviceMessage.Format(new Unordered { Value = "x" }),
            Throws.InvalidOperationException.With.Message.Contains("Unordered.Value needs a field order"));

    [Test]
    public async Task Device_ShouldSendAMessageAndExpectTheReplyByType()
    {
        var transport = new ScriptedTransport(
            DeviceFrame.Text("noise"),
            DeviceMessage.ToFrame(new MeterAck("M-1", Accepted: false)),
            DeviceMessage.ToFrame(new MeterAck("M-1", Accepted: true)));
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddDevices(devices => devices
            .AddClient("Meters", transport, resolveAddress: ProtoDeviceAddress.Template("memory://{deviceId}"))
            .AddDevice<Meter>());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("messages", "00001", TestMethods.Placeholder);

        var ack = await Proto.Context.Devices("Meters").For<Meter>("M-1").ReportAsync(12.5m);

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.Multiple(() =>
        {
            Assert.That(transport.Sent, Has.Count.EqualTo(1));
            Assert.That(transport.Sent[0], Does.StartWith("$MTR,M-1,12.50,"));
            Assert.That(ack, Is.EqualTo(new MeterAck("M-1", true)), "noise and the refused ack were skipped");
        });
    }

    [Test]
    public async Task Device_WhenTheMessageNeverArrives_ShouldNameTheMessageType()
    {
        var transport = new ScriptedTransport(DeviceFrame.Text("noise"));
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddDevices(devices => devices
            .AddClient("Meters", transport, resolveAddress: ProtoDeviceAddress.Template("memory://{deviceId}"))
            .AddDevice<Meter>());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("messages timeout", "00001", TestMethods.Placeholder);

        var exception = Assert.ThrowsAsync<DeviceAssertionException>(async () =>
            await Proto.Context.Devices("Meters").For<Meter>("M-1").ReportAsync(1m, TimeSpan.FromMilliseconds(200)));

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.That(exception!.Message, Does.Contain("MeterAck was not observed").And.Contain("noise"));
    }

    [DeviceMessage("$MTR", Checksum = typeof(NmeaChecksum))]
    private sealed record MeterReading(
        string MeterId,
        [DeviceField(Format = "0.00")] decimal Volume,
        [DeviceField(Format = "yyyyMMddHHmmss")] DateTimeOffset At);

    [DeviceMessage("$ACK")]
    private sealed record MeterAck(string MeterId, bool Accepted);

    [DeviceMessage(Separator = ";")]
    private sealed record Alarm(string Id, [DeviceField(Format = "Y|N")] bool Active, int? Code);

    private enum PumpState
    {
        Stop,
        Run
    }

    [DeviceMessage("P", Separator = "")]
    private sealed class PumpStatus
    {
        [DeviceField(0, Width = 2, Pad = '0', PadLeft = true)]
        public int Pump { get; set; }

        [DeviceField(1, Width = 6)]
        public PumpState State { get; set; }

        [DeviceField(2, Width = 4, Pad = '0', PadLeft = true)]
        public int Flow { get; set; }
    }

    private sealed class Unordered
    {
        [DeviceField]
        public string Value { get; set; } = string.Empty;
    }

    private sealed class Meter : ProtoDevice
    {
        public async Task<MeterAck> ReportAsync(decimal volume, TimeSpan? timeout = null)
        {
            await SendMessageAsync(new MeterReading(Id, volume, DateTimeOffset.UnixEpoch));
            return await ExpectMessageAsync<MeterAck>(ack => ack.Accepted, timeout: timeout);
        }
    }

    private sealed class ScriptedTransport(params DeviceFrame[] replies) : IProtoDeviceTransport
    {
        public string Name => "Scripted";

        public List<string> Sent { get; } = [];

        public ValueTask<IProtoDeviceConnection> ConnectAsync(DeviceEndpoint endpoint, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IProtoDeviceConnection>(new ScriptedConnection(this, replies));

        private sealed class ScriptedConnection : IProtoDeviceConnection
        {
            private readonly ScriptedTransport _transport;
            private readonly Channel<DeviceFrame> _incoming = Channel.CreateUnbounded<DeviceFrame>();

            public ScriptedConnection(ScriptedTransport transport, DeviceFrame[] replies)
            {
                _transport = transport;
                foreach (var reply in replies)
                {
                    _incoming.Writer.TryWrite(reply);
                }
            }

            public string? RemoteAddress => "memory://";

            public ValueTask SendAsync(DeviceFrame frame, CancellationToken cancellationToken = default)
            {
                _transport.Sent.Add(frame.AsText());
                return ValueTask.CompletedTask;
            }

            public async ValueTask<DeviceFrame?> ReceiveAsync(CancellationToken cancellationToken = default)
                => await _incoming.Reader.ReadAsync(cancellationToken);

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
