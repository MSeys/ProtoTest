namespace ProtoTest.Devices.Tests;

using ProtoTest.Core;

/// <summary>
/// Pins the device resource and entity id shape: the device type is part of both, so two
/// device types with the same id on one client coexist, each with its own resource and entity, instead
/// of colliding in the resource registry.
/// </summary>
[TestFixture]
[Category("Characterization")]
public sealed class DeviceResourceIdCharacterizationTests
{
    [Test]
    public async Task TwoDeviceTypesWithTheSameId_ShouldCoexistWithTheirOwnResourceAndEntity()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddDevices(devices => devices
            .AddClient("Chargers", new UnusedTransport(), resolveAddress: ProtoDeviceAddress.Template("memory://cp-001"))
                .AddDevice<FakeCharger>()
                .AddDevice<FakeMeter>());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("same id, two types", "00001", TestMethods.Placeholder);

        var charger = Proto.Context.Devices("Chargers").For<FakeCharger>("CP-001");
        var meter = Proto.Context.Devices("Chargers").For<FakeMeter>("CP-001");
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var entities = host.Trace.Snapshot().Tests.Single().Entities!
            .Where(entity => entity.Kind == ProtoTraceEntityKinds.Device)
            .Select(entity => entity.Id)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(charger.Id, Is.EqualTo("CP-001"));
            Assert.That(meter.Id, Is.EqualTo("CP-001"));
            Assert.That(
                entities,
                Is.EqualTo(new[] { "device:Chargers:FakeCharger:CP-001", "device:Chargers:FakeMeter:CP-001" }),
                "the device type is part of the entity id, so the two types do not share one identity");
        });
    }

    private sealed class FakeCharger : ProtoDevice;

    private sealed class FakeMeter : ProtoDevice;

    private sealed class UnusedTransport : IProtoDeviceTransport
    {
        public string Name => "Unused";

        public ValueTask<IProtoDeviceConnection> ConnectAsync(
            DeviceEndpoint endpoint,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This characterization never connects a device.");
    }
}
