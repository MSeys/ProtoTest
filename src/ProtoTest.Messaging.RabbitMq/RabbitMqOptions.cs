namespace ProtoTest.Messaging.RabbitMq;

using ProtoTest.Core;

/// <summary>RabbitMQ connection choices, layered from <c>ProtoTest:Messaging:RabbitMq</c>.</summary>
public sealed class RabbitMqOptions : IProtoConfigurableOptions
{
    public const string ConfigurationSectionName = "ProtoTest:Messaging:RabbitMq";

    /// <summary>The configuration key a started broker container fills.</summary>
    public const string ConnectionStringSetting = ConfigurationSectionName + ":ConnectionString";

    string IProtoConfigurableOptions.ConfigurationSectionName => ConfigurationSectionName;

    /// <summary>AMQP connection string of the broker, for example the deployed environment's.</summary>
    public string ConnectionString { get; set; } = "amqp://guest:guest@localhost:5672/";

    /// <inheritdoc />
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString) || !IsAmqpUri(ConnectionString))
        {
            throw new ArgumentException(
                $"RabbitMqOptions.ConnectionString must be a non-empty absolute amqp:// or amqps:// URI naming " +
                $"the broker. Set it in code or under '{ConnectionStringSetting}'.",
                nameof(ConnectionString));
        }
    }

    private static bool IsAmqpUri(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (string.Equals(uri.Scheme, "amqp", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, "amqps", StringComparison.OrdinalIgnoreCase));
}
