namespace ProtoTest.Messaging;

using ProtoTest.Core;

/// <summary>Messaging defaults, layered from <c>ProtoTest:Messaging</c> over the code-based registration.</summary>
public sealed class MessagingOptions : IProtoConfigurableOptions
{
    public const string ConfigurationSectionName = "ProtoTest:Messaging";

    string IProtoConfigurableOptions.ConfigurationSectionName => ConfigurationSectionName;

    /// <summary>How long <c>AwaitAsync</c> waits when the caller does not name a timeout.</summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Destinations this suite awaits, configured for the run; an adapter declares the test's per-test
    /// taps from it before the system under test publishes. Declare them in code with
    /// <c>ProtoMessagingBuilder.Tap</c> or under <c>ProtoTest:Messaging:Destinations</c>; configuration
    /// binds after code and can add values. Pre-bind every destination the act publishes to - a tap
    /// declared at the first await misses earlier messages. Optional for brokers that keep history.
    /// </summary>
    public IList<string> Destinations { get; set; } = new List<string>();

    /// <summary>
    /// Destinations this suite declares on the broker - the ones it owns and publishes to itself -
    /// configured for the run; an adapter that owns broker topology creates each of them during test
    /// setup, before any tap binds. Declare them in code with <c>ProtoMessagingBuilder.Declare</c> or
    /// under <c>ProtoTest:Messaging:DeclaredDestinations</c>; configuration binds after code and can
    /// add values. Declaration is idempotent: an existing destination is left as it is and a repeated
    /// declaration is a no-op. A broker whose destinations always exist (the in-memory broker) treats
    /// a declaration as a no-op.
    /// </summary>
    public IList<string> DeclaredDestinations { get; set; } = new List<string>();

    /// <inheritdoc />
    public void Validate()
    {
        if (DefaultTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DefaultTimeout),
                DefaultTimeout,
                "MessagingOptions.DefaultTimeout must be greater than zero.");
        }
    }
}
