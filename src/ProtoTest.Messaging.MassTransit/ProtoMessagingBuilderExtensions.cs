namespace ProtoTest.Messaging.MassTransit;

using ProtoTest.Core;
using ProtoTest.Messaging;
using ProtoTest.Messaging.MassTransit.Internal;

public static class ProtoMessagingBuilderExtensions
{
    /// <summary>
    /// Uses the application's MassTransit test harness as the broker: the suite publishes and awaits
    /// messages over the <c>ITestHarness</c> the in-process application registered with
    /// <c>AddMassTransitTestHarness</c>, so the messages the application publishes through its own
    /// <c>IPublishEndpoint</c> are the ones a test awaits. The destination names a message contract type
    /// (its full name, its short name, or its <c>urn:message:</c> URN); the payload is JSON for that
    /// contract.
    /// </summary>
    /// <remarks>
    /// The harness resolves from the application's in-process server (<c>AddAspNetCoreServer</c>), so
    /// register <c>AddMessaging</c> after the application's server - the messaging client initializes
    /// once the server exists, exactly like the in-process topology a RabbitMQ tap binds to. The
    /// <c>Broker</c> capability is declared only while
    /// <c>ProtoTest:Applications:{application}:BaseUrl</c> is not configured: a published application
    /// has no test harness in this process, so the capability is absent and
    /// <c>[RequiresCapability(ProtoCapabilityKinds.Broker)]</c> skips instead of failing at setup.
    /// </remarks>
    /// <typeparam name="TProgram">The entry point class of the in-process application under test.</typeparam>
    /// <param name="messaging">The messaging builder to attach the adapter to.</param>
    /// <param name="application">
    /// The application whose in-process server hosts the harness, as registered with
    /// <c>AddAspNetCoreServer</c> or <c>AddApplication</c>; defaults to <c>Default</c>.
    /// </param>
    public static ProtoMessagingBuilder UseMassTransit<TProgram>(
        this ProtoMessagingBuilder messaging,
        string application = "Default")
        where TProgram : class
    {
        ArgumentNullException.ThrowIfNull(messaging);
        ArgumentException.ThrowIfNullOrWhiteSpace(application);

        // The harness exists only while the application is hosted in-process, so the capability needs
        // the address to be absent: a configured BaseUrl means a published process with no test harness
        // here, and gated tests skip instead of failing at first publish or await.
        return messaging.UseBrokerUnlessConfigured(
            _ => new MassTransitMessageBroker<TProgram>(application),
            $"{ProtoApplication.SectionPath}:{application}:BaseUrl");
    }
}
