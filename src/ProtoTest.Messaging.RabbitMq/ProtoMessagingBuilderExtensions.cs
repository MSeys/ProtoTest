namespace ProtoTest.Messaging.RabbitMq;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoTest.Core;
using ProtoTest.Messaging;

public static class ProtoMessagingBuilderExtensions
{
    private static readonly string DefaultConnectionString = new RabbitMqOptions().ConnectionString;

    /// <summary>
    /// Uses RabbitMQ as the broker: publish to exchanges named like the destination, await on a tap
    /// prepared for the destination before the act. The connection comes from
    /// <c>ProtoTest:Messaging:RabbitMq:ConnectionString</c>, so a deployed run only changes configuration.
    /// </summary>
    /// <remarks>
    /// The <c>Broker</c> capability is declared conditionally on that key: a run whose address comes
    /// from configuration or from a registered broker container keeps the capability, while a run with
    /// neither loses it, so <c>[RequiresCapability(ProtoCapabilityKinds.Broker)]</c> skips instead of
    /// failing at setup or first publish. A callback that sets <see cref="RabbitMqOptions.ConnectionString"/>
    /// itself provides the address in code, so the capability stays unconditional.
    /// </remarks>
    public static ProtoMessagingBuilder UseRabbitMq(
        this ProtoMessagingBuilder messaging,
        Action<RabbitMqOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(messaging);
        // The first RabbitMQ registration wins, like every other option; a repeated call cannot replace it.
        messaging.Services.TryAddSingleton(serviceProvider =>
        {
            var options = ProtoOptionsRegistration.Resolve<RabbitMqOptions>(serviceProvider, configure);

            // Precedence: an explicitly configured connection string wins, then a started broker
            // container, then whatever the registration or the defaults chose.
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var configured = configuration[RabbitMqOptions.ConnectionStringSetting];
            if (!string.IsNullOrWhiteSpace(configured))
            {
                options.ConnectionString = configured;
            }
            else if (serviceProvider.GetService<ProtoInfrastructureSettings>() is { } settings
                && settings.Values.TryGetValue(RabbitMqOptions.ConnectionStringSetting, out var provided))
            {
                options.ConnectionString = provided;
            }

            return options;
        });
        return messaging.UseBroker(
            serviceProvider => new RabbitMqMessageBroker(serviceProvider.GetRequiredService<RabbitMqOptions>()),
            AddressKeys(configure));
    }

    /// <summary>
    /// The connection-string key the adapter reads, unless the callback itself provides a connection
    /// string: a code-provided address cannot be withdrawn by configuration, so the capability stays
    /// unconditional. The callback runs once here to see what it provides and again when the options
    /// resolve.
    /// </summary>
    private static string[] AddressKeys(Action<RabbitMqOptions>? configure)
    {
        if (configure is null)
        {
            return [RabbitMqOptions.ConnectionStringSetting];
        }

        var probe = new RabbitMqOptions();
        configure(probe);
        var providedInCode = !string.IsNullOrWhiteSpace(probe.ConnectionString)
            && !string.Equals(probe.ConnectionString, DefaultConnectionString, StringComparison.Ordinal);
        return providedInCode ? [] : [RabbitMqOptions.ConnectionStringSetting];
    }
}
