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
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            options.BindFromConfiguration(configuration);

            // Precedence: an explicitly configured connection string wins, then a started broker
            // container, then whatever the registration or the defaults chose.
            var configured = configuration[ProtoRabbitMqOptions.ConnectionStringSetting];
            if (!string.IsNullOrWhiteSpace(configured))
            {
                options.ConnectionString = configured;
            }
            else if (serviceProvider.GetService<ProtoInfrastructureSettings>() is { } settings
                && settings.Values.TryGetValue(ProtoRabbitMqOptions.ConnectionStringSetting, out var provided))
            {
                options.ConnectionString = provided;
            }

            return options;
        });
        return messaging.UseBroker(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<ProtoRabbitMqOptions>();
            return new RabbitMqMessageBroker(options);
        });
    }
}
