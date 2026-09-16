namespace ProtoTest.Web.Playwright;

using Microsoft.Playwright;

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

public sealed class PlaywrightWebOptions
{
    public PlaywrightBrowser Browser { get; set; } = PlaywrightBrowser.Chromium;
    public bool Headless { get; set; } = true;
    public float? SlowMo { get; set; }
    public string? Channel { get; set; }
    public BrowserNewContextOptions Context { get; set; } = new();
    public PlaywrightTraceRetention TraceRetention { get; set; } = PlaywrightTraceRetention.OnWebFailure;
    public bool CorrelateTraceGroups { get; set; } = true;
    public PlaywrightConsoleCapture ConsoleCapture { get; set; } = PlaywrightConsoleCapture.WarningsAndErrors;
    public bool CapturePageErrors { get; set; } = true;
    public bool CaptureRequestFailures { get; set; } = true;
}
