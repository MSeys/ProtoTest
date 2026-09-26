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
    /// The <paramref name="configure"/> callback runs exactly once, when this call registers; the
    /// instance it configured is the one the run resolves.
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

        // The callback runs exactly once, here: the options it configured are what the run resolves, so
        // a code-provided address is visible both to the capability decision and to the adapter, and a
        // callback with side effects cannot run twice.
        var options = new RabbitMqOptions();
        configure?.Invoke(options);
        var providedInCode = !string.IsNullOrWhiteSpace(options.ConnectionString)
            && !string.Equals(options.ConnectionString, DefaultConnectionString, StringComparison.Ordinal);

        // The first RabbitMQ registration wins, like every other option; a repeated call cannot replace it.
        messaging.Services.TryAddSingleton(serviceProvider =>
        {
            var resolved = ProtoOptionsRegistration.Resolve(serviceProvider, () => options);

            // Precedence: an explicitly configured connection string wins, then a started broker
            // container, then whatever the registration or the defaults chose.
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var configured = configuration[RabbitMqOptions.ConnectionStringSetting];
            if (!string.IsNullOrWhiteSpace(configured))
            {
                resolved.ConnectionString = configured;
            }
            else if (serviceProvider.GetService<ProtoInfrastructureSettings>() is { } settings
                && settings.Values.TryGetValue(RabbitMqOptions.ConnectionStringSetting, out var provided))
            {
                resolved.ConnectionString = provided;
            }

            // Re-validate the final value: the settings a started broker container publishes are applied
            // after ProtoOptionsRegistration already validated, and a malformed one must fail here naming
            // the key rather than at connect time.
            resolved.Validate();
            return resolved;
        });
        return messaging.UseBroker(
            serviceProvider => new RabbitMqMessageBroker(serviceProvider.GetRequiredService<RabbitMqOptions>()),
            providedInCode ? [] : [RabbitMqOptions.ConnectionStringSetting]);
    }
}
