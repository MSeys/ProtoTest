namespace ProtoTest.Devices.Mqtt;

using ProtoTest.Core;
using ProtoTest.Devices;

/// <summary>Registers an MQTT device client with <c>AddDevices</c>.</summary>
public static class MqttDeviceBuilderExtensions
{
    /// <summary>
    /// Declares a named device client over MQTT. The topic templates address one device each -
    /// <c>{deviceId}</c> is filled from the id passed to <c>For&lt;TDevice&gt;("id")</c> - so the
    /// device publishes to <paramref name="publishTopic"/> and receives what
    /// <paramref name="subscribeTopic"/> matches (its wildcards included). The registration carries
    /// both as endpoint settings; a client registered directly with <c>AddClient</c> may instead name
    /// <c>publishTopic</c>/<c>subscribeTopic</c> in the address query, and a setting wins over the
    /// address parameter. The broker address resolves through the devices precedence: a setting a
    /// started piece published (a broker container) wins over configuration
    /// (<c>ProtoTest:Devices:Mqtt:Broker</c>), which wins over <paramref name="address"/>;
    /// <paramref name="resolveAddress"/> replaces that rule for a suite that resolves the broker itself.
    /// </summary>
    public static ProtoDeviceClientBuilder AddMqttClient(
        this ProtoDeviceBuilder devices,
        string name,
        string publishTopic,
        string subscribeTopic,
        string? address = null,
        Func<ProtoExecutionContext, string, string>? resolveAddress = null,
        Action<MqttDeviceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(publishTopic);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscribeTopic);
        if (publishTopic.Contains('+', StringComparison.Ordinal) || publishTopic.Contains('#', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The publish topic '{publishTopic}' cannot contain the '+' or '#' wildcards; a publish addresses one topic.",
                nameof(publishTopic));
        }

        ProtoOptionsRegistration.Configure<MqttDeviceOptions>(devices.Services, () => new MqttDeviceOptions(), configure);
        devices.AddTransport<MqttDeviceTransport>(MqttDeviceTransport.TransportName);

        var client = devices.AddClient(
            name,
            MqttDeviceTransport.TransportName,
            path: null,
            resolveAddress: (context, deviceId) =>
            {
                // Resolving the options here fails a bad configuration when the device is created,
                // not at the first connect.
                var options = context.Service<MqttDeviceOptions>();
                var broker = resolveAddress is not null
                    ? resolveAddress(context, deviceId)
                    : ProtoApplication.ResolveSetting(
                            context.Configuration,
                            context.TryService<ProtoInfrastructureSettings>(),
                            MqttDeviceOptions.BrokerSetting)
                        ?? options.Broker
                        ?? address;
                if (string.IsNullOrWhiteSpace(broker))
                {
                    throw new InvalidOperationException(
                        $"MQTT device client '{name}' has no broker address for '{deviceId}'. Pass one to " +
                        $"AddMqttClient(address: ...), register a resolver, or set '{MqttDeviceOptions.BrokerSetting}' " +
                        $"(a broker container registered with UseContainer fills it for the run).");
                }

                return broker;
            });

        // The topics ride the endpoint settings the client resolves per device; the address query
        // parameters stay the fallback for a client registered directly with AddClient.
        var registered = client
            .WithSetting(MqttDeviceAddress.PublishTopicParameter, publishTopic)
            .WithSetting(MqttDeviceAddress.SubscribeTopicParameter, subscribeTopic);

        // An explicit address or resolver serves without configuration, so the capabilities stay
        // unconditional; otherwise they follow MqttDeviceOptions.BrokerSetting like every address-driven
        // integration, so an addressless client whose broker no key can provide drops them.
        return address is null && resolveAddress is null
            ? registered.WithAddressKeys(MqttDeviceOptions.BrokerSetting)
            : registered;
    }
}
