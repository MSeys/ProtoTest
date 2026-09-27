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
    public TimeSpan ActionTimeout { get; set; } = WebBackendDefaults.DefaultTimeout;

    /// <summary>
    /// Downloads the selected browser through the Playwright driver before the first launch, so a clean
    /// machine or CI runner needs no separate install step. Ignored when <see cref="Channel"/> names a
    /// system browser (for example <c>msedge</c> or <c>chrome</c>).
    /// </summary>
    public bool InstallBrowsers { get; set; }

    /// <summary>The context viewport width; both dimensions must be set for a viewport to apply.</summary>
    public int? ViewportWidth { get; set; }

    /// <summary>The context viewport height; both dimensions must be set for a viewport to apply.</summary>
    public int? ViewportHeight { get; set; }

    /// <summary>The locale the context reports, for example <c>en-US</c>.</summary>
    public string? Locale { get; set; }

    /// <summary>The IANA timezone the context reports, for example <c>Europe/Brussels</c>.</summary>
    public string? TimezoneId { get; set; }

    /// <summary>A user agent override for the context.</summary>
    public string? UserAgent { get; set; }

    /// <summary>A storage-state JSON file the context starts from.</summary>
    public string? StorageStatePath { get; set; }

    /// <summary>
    /// Escape hatch: adjusts the native context options after the settings above are applied, for the
    /// Playwright knobs ProtoTest does not model.
    /// </summary>
    public Action<BrowserNewContextOptions>? ConfigureContext { get; set; }

    public PlaywrightTraceRetention TraceRetention { get; set; } = PlaywrightTraceRetention.OnWebFailure;
    public bool CorrelateTraceGroups { get; set; } = true;
    public PlaywrightConsoleCapture ConsoleCapture { get; set; } = PlaywrightConsoleCapture.WarningsAndErrors;
    public bool CapturePageErrors { get; set; } = true;
    public bool CaptureRequestFailures { get; set; } = true;

    /// <summary>
    /// The largest native trace attached to the test, in bytes. Defaults to 32 MiB; a bigger trace is
    /// not attached and the trace records <c>web.playwright.trace_too_large</c> with its size, so a
    /// failing run cannot spike memory buffering the whole zip. Set to zero to read without a cap.
    /// </summary>
    public long MaxTraceBytes { get; set; } = 32 * 1024 * 1024;

    /// <summary>The native context options this configuration describes, escape hatch applied last.</summary>
    internal BrowserNewContextOptions BuildContextOptions()
    {
        var context = new BrowserNewContextOptions();
        if (ViewportWidth is { } width && ViewportHeight is { } height)
        {
            context.ViewportSize = new ViewportSize { Width = width, Height = height };
        }

        if (Locale is not null) context.Locale = Locale;
        if (TimezoneId is not null) context.TimezoneId = TimezoneId;
        if (UserAgent is not null) context.UserAgent = UserAgent;
        if (StorageStatePath is not null) context.StorageStatePath = StorageStatePath;
        ConfigureContext?.Invoke(context);
        return context;
    }

    internal static void Validate(PlaywrightWebOptions options)
    {
        if (options.ActionTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ActionTimeout));
        if (options.MaxTraceBytes < 0) throw new ArgumentOutOfRangeException(nameof(MaxTraceBytes));
    }
}
