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

    /// <summary>How often an await checks for a new message.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(25);
}
