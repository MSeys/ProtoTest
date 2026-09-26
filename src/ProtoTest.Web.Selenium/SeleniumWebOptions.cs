namespace ProtoTest.Web.Selenium;

using ProtoTest.Core;
using ProtoTest.Web.Internal;

public enum SeleniumDiagnosticTraceRetention
{
    Off,
    OnWebFailure,
    Always
}

/// <summary>
/// Selenium session options. Besides code, they bind from <c>ProtoTest:Web:Selenium</c>.
/// </summary>
public sealed class SeleniumWebOptions : IProtoConfigurableOptions
{
    public const string BackendName = "Selenium";

    public const string ConfigurationSectionName = "ProtoTest:Web:Selenium";

    string IProtoConfigurableOptions.ConfigurationSectionName => ConfigurationSectionName;

    public TimeSpan ActionTimeout { get; set; } = WebTiming.DefaultTimeout;

    /// <summary>
    /// How often an action retry and a session element assertion probe. One setting: the backend's
    /// action probes and the session's assertions both read it, so a session cannot wait faster or
    /// slower than the backend retries.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = WebTiming.DefaultPollInterval;
    public bool WaitForStableBounds { get; set; } = true;
    public bool CheckClickObstruction { get; set; } = true;
    public SeleniumDiagnosticTraceRetention DiagnosticTraceRetention { get; set; } = SeleniumDiagnosticTraceRetention.OnWebFailure;

    internal static void Validate(SeleniumWebOptions options)
    {
        if (options.ActionTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ActionTimeout));
        if (options.PollInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(PollInterval));
    }
}
