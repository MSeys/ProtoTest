namespace ProtoTest.Web.Selenium;

public enum SeleniumDiagnosticTraceRetention
{
    Off,
    OnWebFailure,
    Always
}

public sealed class SeleniumWebOptions
{
    public TimeSpan ActionTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(50);
    public bool WaitForStableBounds { get; set; } = true;
    public bool CheckClickObstruction { get; set; } = true;
    public SeleniumDiagnosticTraceRetention DiagnosticTraceRetention { get; set; } = SeleniumDiagnosticTraceRetention.OnWebFailure;
}
