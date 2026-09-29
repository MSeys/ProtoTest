namespace ProtoTest.Devices.Mqtt.Tests;

using Microsoft.Extensions.Configuration;
using ProtoTest.Core;
using ProtoTest.Devices.Mqtt;

[TestFixture]
[Category("Characterization")]
public sealed class MqttRegistrationTests
{
    [Test]
    public async Task AddMqttClient_ShouldRegisterTheTransportAndDeviceCapabilities_Characterization()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddDevices(devices => devices
            .AddMqttClient("Sensors", "sensors/{deviceId}/out", "sensors/{deviceId}/in", address: "mqtt://127.0.0.1:1883")
                .AddDevice<PingDevice>()
                .AddProtocol<PingProtocol>());
        await using var host = builder.Build();
        await host.StartAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                host.HasCapability(ProtoCapabilityKinds.Device, MqttDeviceTransport.TransportName),
                Is.True,
                "the transport declares its device capability under its transport name");
            Assert.That(
                host.HasCapability(ProtoCapabilityKinds.Device, nameof(PingDevice)),
                Is.True,
                "the device type keeps its capability, so [RequiresDevice] gates stay honest");
        }

        await host.StopAsync();
    }

    [Test]
    public void AddMqttClient_WhenThePublishTopicCarriesAWildcard_ShouldThrowAtRegistration()
    {
        var builder = new ProtoHostBuilder();
        var exception = Assert.Throws<ArgumentException>(() => builder.AddDevices(devices => devices
            .AddMqttClient("Sensors", "sensors/#", "sensors/{deviceId}/in", address: "mqtt://127.0.0.1:1883")));

        Assert.That(exception!.Message, Does.Contain("wildcards"));
    }

    [Test]
    public async Task AddMqttClient_WithoutABrokerAddress_ShouldFailNamingTheSetting()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddDevices(devices => devices
            .AddMqttClient("Sensors", "sensors/{deviceId}/out", "sensors/{deviceId}/in")
                .AddDevice<PingDevice>());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("mqtt missing broker", "00001", TestMethods.Placeholder);

        var exception = Assert.Throws<InvalidOperationException>(
            () => Proto.Context.Devices("Sensors").For<PingDevice>("S-001"));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain(MqttDeviceOptions.BrokerSetting));
            Assert.That(exception.Message, Does.Contain("AddMqttClient"));
        }
    }

    [Test]
    public async Task AddMqttClient_ShouldPreferTheConfiguredBrokerOverTheRegistrationAddress()
    {
        var configuredPort = TestNetworking.FreePort();
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [MqttDeviceOptions.BrokerSetting] = $"mqtt://127.0.0.1:{configuredPort}"
            }));
        builder.AddDevices(devices => devices
            .AddMqttClient(
                "Sensors",
                "sensors/{deviceId}/out",
                "sensors/{deviceId}/in",
                address: "mqtt://127.0.0.1:2")
                .AddDevice<PingDevice>());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("mqtt broker precedence", "00001", TestMethods.Placeholder);

        var device = Proto.Context.Devices("Sensors").For<PingDevice>("S-002");
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await device.InitializeAsync());

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        await host.StopAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain($"127.0.0.1:{configuredPort}"));
            Assert.That(
                exception.Message,
                Does.Not.Contain("127.0.0.1:2"),
                "the configured broker wins over the registration's code default");
        }
    }

    [Test]
    public async Task AddMqttClient_ShouldUseTheResolverAddress()
    {
        var port = TestNetworking.FreePort();
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddDevices(devices => devices
            .AddMqttClient(
                "Sensors",
                "sensors/{deviceId}/out",
                "sensors/{deviceId}/in",
                resolveAddress: (_, deviceId) => $"mqtt://127.0.0.1:{port}/resolved/{deviceId}")
                .AddDevice<PingDevice>());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("mqtt resolver topics", "00001", TestMethods.Placeholder);

        var device = Proto.Context.Devices("Sensors").For<PingDevice>("S-009");
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await device.InitializeAsync());

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        await host.StopAsync();

        var entity = host.Trace.Snapshot().Tests.Single().Entities!
            .Single(candidate => candidate.Kind == ProtoTraceEntityKinds.Device);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("/resolved/S-009"), "the resolver's address is used");
            Assert.That(
                entity.State["device.address"],
                Is.EqualTo($"mqtt://127.0.0.1:{port}/resolved/S-009"),
                "the endpoint keeps the resolver's broker address; the topics travel as settings");
        }
    }

    [TestCase(MqttDeviceOptions.BrokerSetting, "http://127.0.0.1:1883", "mqtt://")]
    [TestCase("ProtoTest:Devices:Mqtt:ConnectTimeout", "00:00:00", "connect timeout")]
    [TestCase("ProtoTest:Devices:Mqtt:KeepAlivePeriod", "-00:00:01", "keep-alive")]
    [TestCase("ProtoTest:Devices:Mqtt:MaxPacketBytes", "-1", "maximum packet size")]
    public async Task MqttDeviceOptions_WhenConfiguredWithAnInvalidValue_ShouldFailWhenTheDeviceIsCreated(
        string key,
        string value,
        string expected)
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { [key] = value }));
        builder.AddDevices(devices => devices
            .AddMqttClient("Sensors", "sensors/{deviceId}/out", "sensors/{deviceId}/in")
                .AddDevice<PingDevice>());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("mqtt options validation", "00001", TestMethods.Placeholder);

        var exception = Assert.Catch<ArgumentException>(
            () => Proto.Context.Devices("Sensors").For<PingDevice>("S-003"));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.That(exception!.Message, Does.Contain(expected).IgnoreCase);
    }
}
