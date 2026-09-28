namespace ProtoTest.Devices.Mqtt;

/// <summary>
/// The address a device endpoint carries for MQTT: an <c>mqtt://host:port</c> broker address. The
/// topics come from the endpoint settings the registration filled; a hand-registered endpoint may
/// instead name them in the address query, and a setting wins over the address parameter.
/// </summary>
internal sealed record MqttEndpointTarget(
    string Broker,
    string Host,
    int Port,
    string PublishTopic,
    string SubscribeTopic);

/// <summary>Resolves the MQTT device endpoint: broker, publish topic and subscribe filter.</summary>
internal static class MqttDeviceAddress
{
    /// <summary>
    /// The endpoint setting (and address query parameter) carrying the topic a device publishes to.
    /// <c>AddMqttClient</c> fills the setting; the query parameter is the fallback for an endpoint
    /// registered directly with <c>AddClient</c>.
    /// </summary>
    internal const string PublishTopicParameter = "publishTopic";

    /// <summary>
    /// The endpoint setting (and address query parameter) carrying the topic filter a device subscribes
    /// to. <c>AddMqttClient</c> fills the setting; the query parameter is the fallback for an endpoint
    /// registered directly with <c>AddClient</c>.
    /// </summary>
    internal const string SubscribeTopicParameter = "subscribeTopic";

    /// <summary>The port an <c>mqtt://</c> address without one opens.</summary>
    internal const int DefaultPort = 1883;

    internal static MqttEndpointTarget Parse(string address, IReadOnlyDictionary<string, string?>? settings = null)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "mqtt", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new InvalidOperationException(
                $"'{address}' is not an MQTT device address; register the client with AddMqttClient(...), " +
                $"which resolves an mqtt://host:port broker address and carries the device's topics as settings.");
        }

        var publish = Setting(settings, PublishTopicParameter) ?? QueryValue(uri, PublishTopicParameter);
        var subscribe = Setting(settings, SubscribeTopicParameter) ?? QueryValue(uri, SubscribeTopicParameter);
        if (string.IsNullOrWhiteSpace(publish) || string.IsNullOrWhiteSpace(subscribe))
        {
            throw new InvalidOperationException(
                $"The device address '{address}' names no publish and subscribe topic; register the client " +
                $"with AddMqttClient(name, publishTopic, subscribeTopic, ...), or name both as the " +
                $"'{PublishTopicParameter}' and '{SubscribeTopicParameter}' address query parameters.");
        }

        if (publish.Contains('+', StringComparison.Ordinal) || publish.Contains('#', StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The publish topic '{publish}' cannot contain the '+' or '#' wildcards; a publish addresses one topic.");
        }

        return new MqttEndpointTarget(
            uri.GetLeftPart(UriPartial.Authority),
            uri.Host,
            uri.Port < 0 ? DefaultPort : uri.Port,
            publish,
            subscribe);
    }

    private static string? Setting(IReadOnlyDictionary<string, string?>? settings, string key)
        => settings is not null && settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static string? QueryValue(Uri uri, string name)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            if (string.Equals(Uri.UnescapeDataString(pair[..separator]), name, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(pair[(separator + 1)..]);
            }
        }

        return null;
    }
}
