namespace ProtoTest.Messaging;

using ProtoTest.Core;

/// <summary>Messaging defaults, layered from <c>ProtoTest:Messaging</c> over the code-based registration.</summary>
public sealed class ProtoMessagingOptions : IProtoConfigurableOptions
{
    public const string ConfigurationSectionName = "ProtoTest:Messaging";

    string IProtoConfigurableOptions.ConfigurationSectionName => ConfigurationSectionName;

    /// <summary>How long <c>AwaitAsync</c> waits when the caller does not name a timeout.</summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Destinations this suite awaits, declared at test setup so an adapter can bind its tap before the
    /// system under test publishes. Optional for brokers that keep history.
    /// </summary>
    public IList<string> Destinations { get; set; } = new List<string>();
}
