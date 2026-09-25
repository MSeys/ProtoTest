namespace ProtoTest.Devices.Tests;

using ProtoTest.Core;

/// <summary>
/// Pins the device resource id shape (audit Stage A0): <c>device:{client}:{id}</c> omits the device
/// type, so a second device type with the same id collides in the resource registry even though the
/// client registry is keyed by type.
/// </summary>
[TestFixture]
[Category("Characterization")]
public sealed class DeviceResourceIdCharacterizationTests
{
    [Test]
    public async Task TwoDeviceTypesWithTheSameId_ShouldFailOnTheSecondRegistration()
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
        var exception = Assert.Throws<InvalidOperationException>(
            () => Proto.Context.Devices("Chargers").For<FakeMeter>("CP-001"));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            // Pins current behavior; audit DEV-4 flips this once the resource id includes the device
            // type, when both types can share one id on one client.
            Assert.That(charger.Id, Is.EqualTo("CP-001"));
            Assert.That(
                exception!.Message,
                Is.EqualTo("A resource with id 'device:Chargers:CP-001' is already registered in the current ProtoExecutionContext."));
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
