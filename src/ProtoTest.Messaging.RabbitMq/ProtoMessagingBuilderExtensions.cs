namespace ProtoTest.Messaging.RabbitMq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using ProtoTest.Core;
using ProtoTest.Messaging;

public static class ProtoMessagingBuilderExtensions
{
    /// <summary>
    /// Uses RabbitMQ as the broker: publish to exchanges named like the destination, await with a
    /// per-test tap queue. The connection comes from <c>ProtoTest:Messaging:RabbitMq:ConnectionString</c>,
    /// so a deployed run only changes configuration.
    /// </summary>
    public static ProtoMessagingBuilder UseRabbitMq(
        this ProtoMessagingBuilder messaging,
        Action<ProtoRabbitMqOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(messaging);
        messaging.Services.AddSingleton(serviceProvider =>
        {
            var options = new ProtoRabbitMqOptions();
            configure?.Invoke(options);
            options.BindFromConfiguration(serviceProvider.GetRequiredService<IConfiguration>());
            return options;
        });
        return messaging.UseBroker(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<ProtoRabbitMqOptions>();
            return new RabbitMqMessageBroker(options);
        });
    }
}
