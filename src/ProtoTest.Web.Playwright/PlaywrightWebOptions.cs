namespace ProtoTest.Web.Playwright;

using Microsoft.Playwright;
using ProtoTest.Core;

public enum PlaywrightBrowser
{
    Chromium,
    Firefox,
    Webkit
}

public enum PlaywrightTraceRetention
{
    Off,
    OnWebFailure,
    Always
}

public enum PlaywrightConsoleCapture
{
    Off,
    Errors,
    WarningsAndErrors,
    All
}

/// <summary>
/// Playwright session options. Besides code, they bind from <c>ProtoTest:Web:Playwright</c> and
/// <c>ProtoTest:Web:Sessions:{name}</c>, in that order.
/// </summary>
public sealed class PlaywrightWebOptions : IProtoConfigurableOptions
{
    public const string BackendName = "Playwright";

    string IProtoConfigurableOptions.ConfigurationSectionName => $"ProtoTest:Web:{BackendName}";

    public PlaywrightBrowser Browser { get; set; } = PlaywrightBrowser.Chromium;
    public bool Headless { get; set; } = true;
    public float? SlowMo { get; set; }
    public string? Channel { get; set; }

    /// <summary>
    /// Downloads the selected browser through the Playwright driver before the first launch, so a clean
    /// machine or CI runner needs no separate install step. Ignored when <see cref="Channel"/> names a
    /// system browser (for example <c>msedge</c> or <c>chrome</c>).
    /// </summary>
    public bool InstallBrowsers { get; set; }
    public BrowserNewContextOptions Context { get; set; } = new();
    public PlaywrightTraceRetention TraceRetention { get; set; } = PlaywrightTraceRetention.OnWebFailure;
    public bool CorrelateTraceGroups { get; set; } = true;
    public PlaywrightConsoleCapture ConsoleCapture { get; set; } = PlaywrightConsoleCapture.WarningsAndErrors;
    public bool CapturePageErrors { get; set; } = true;
    public bool CaptureRequestFailures { get; set; } = true;
}
