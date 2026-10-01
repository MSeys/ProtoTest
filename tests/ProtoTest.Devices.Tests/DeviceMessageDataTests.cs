namespace ProtoTest.Devices.Tests;

using ProtoTest.Core;
using ProtoTest.Data;

[TestFixture]
public sealed class DeviceMessageDataTests
{
    [Test]
    public async Task DataBuilders_ShouldBuildMessagesWithDefaultsFromTheTestClock()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddData(data => data.For<MeterReading>()
            .Default(reading => reading.MeterId, context => $"M-{context.ObjectSequence}")
            .Default(reading => reading.Volume, 1m)
            .Default(reading => reading.At, context => context.Clock.GetUtcNow()));
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("device data", "00001", TestMethods.Placeholder);

        var start = Proto.Context.Clock.GetUtcNow();
        var first = Proto.Context.Data().For<MeterReading>().With(reading => reading.Volume, 12.5m).Build();
        Proto.Context.Clock.Advance(TimeSpan.FromMinutes(15));
        var series = Proto.Context.Data().For<MeterReading>().BuildMany(3);
        var lines = series.Select(DeviceMessage.Format).ToArray();

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.Multiple(() =>
        {
            Assert.That(first.Volume, Is.EqualTo(12.5m));
            Assert.That(first.At, Is.EqualTo(start), "the default stamp reads the test clock");
            Assert.That(series.Select(reading => reading.At), Is.All.EqualTo(start.AddMinutes(15)), "and follows it when the test advances it");
            Assert.That(series.Select(reading => reading.MeterId).Distinct().Count(), Is.EqualTo(3));
            Assert.That(
                lines.Select(line => DeviceMessage.Parse<MeterReading>(line)),
                Is.EqualTo(series.Select(reading => reading with { At = WholeSeconds(reading.At) })),
                "the format has no fractions, so a parsed stamp is the whole second");
        });
    }

    private static DateTimeOffset WholeSeconds(DateTimeOffset value) => value.AddTicks(-(value.Ticks % TimeSpan.TicksPerSecond));

    [DeviceMessage("$MTR")]
    [DeviceChecksum<NmeaChecksum>]
    public sealed record MeterReading(
        string MeterId,
        [DeviceField(Format = "0.00")] decimal Volume,
        [DeviceField(Format = "yyyyMMddHHmmss")] DateTimeOffset At);
}
