namespace ProtoTest.Web.Selenium;

using ProtoTest.Core;

public enum SeleniumDiagnosticTraceRetention
{
    Off,
    OnWebFailure,
    Always
}

/// <summary>
/// Selenium session options. Besides code, they bind from <c>ProtoTest:Web:Selenium</c> and
/// <c>ProtoTest:Web:Sessions:{name}</c>, in that order.
/// </summary>
public sealed class SeleniumWebOptions : IProtoConfigurableOptions
{
    public const string BackendName = "Selenium";

    public const string ConfigurationSectionName = "ProtoTest:Web:Selenium";

    string IProtoConfigurableOptions.ConfigurationSectionName => ConfigurationSectionName;

    public TimeSpan ActionTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(50);
    public bool WaitForStableBounds { get; set; } = true;
    public bool CheckClickObstruction { get; set; } = true;
    public SeleniumDiagnosticTraceRetention DiagnosticTraceRetention { get; set; } = SeleniumDiagnosticTraceRetention.OnWebFailure;

    internal static void Validate(SeleniumWebOptions options)
    {
        if (options.ActionTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ActionTimeout));
        if (options.PollInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(PollInterval));
    }
}
