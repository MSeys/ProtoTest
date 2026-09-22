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
/// Playwright session options. Besides code, they bind from <c>ProtoTest:Web:Playwright</c>.
/// </summary>
public sealed class PlaywrightWebOptions : IProtoConfigurableOptions
{
    public const string BackendName = "Playwright";

    public const string ConfigurationSectionName = "ProtoTest:Web:Playwright";

    string IProtoConfigurableOptions.ConfigurationSectionName => ConfigurationSectionName;

    public PlaywrightBrowser Browser { get; set; } = PlaywrightBrowser.Chromium;
    public bool Headless { get; set; } = true;
    public float? SlowMo { get; set; }
    public string? Channel { get; set; }

    /// <summary>
    /// How long one read or action waits for its element before the backend reports it as missing.
    /// Playwright's own default is 30 seconds, which is longer than a polling assertion's budget; this
    /// matches the Selenium option of the same name so the two backends fail at the same speed.
    /// </summary>
    public TimeSpan ActionTimeout { get; set; } = TimeSpan.FromSeconds(5);

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

    internal static void Validate(PlaywrightWebOptions options)
    {
        if (options.ActionTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ActionTimeout));
    }
}
