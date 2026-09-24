namespace ProtoTest.Json;

using ProtoTest.Core;

/// <summary>
/// Capture and redaction options every protocol's attachment configuration shares: whether request
/// bodies, responses and expected shapes are captured, plus the redaction settings from
/// <see cref="JsonDiagnosticOptions"/>. A protocol adds only what is genuinely its own, such as HTTP's
/// sensitive headers and query parameters.
/// </summary>
public class ProtoDiagnosticCaptureOptions : JsonDiagnosticOptions, IProtoConfigurableOptions
{
    /// <summary>Creates options bound to a known configuration section, e.g. <c>ProtoTest:Rest:Attachments</c>.</summary>
    public ProtoDiagnosticCaptureOptions(string configurationSectionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationSectionName);
        ConfigurationSectionName = configurationSectionName;
    }

    /// <summary>Captures outgoing request bodies as attachments. Defaults to <see langword="true"/>.</summary>
    public bool CaptureRequestBodies { get; set; } = true;

    /// <summary>Captures response bodies as attachments. Defaults to <see langword="true"/>.</summary>
    public bool CaptureResponses { get; set; } = true;

    /// <summary>Captures the expected shape used by shape assertions. Defaults to <see langword="true"/>.</summary>
    public bool CaptureExpectedShapes { get; set; } = true;

    /// <summary>The configuration section this instance binds from, e.g. "ProtoTest:Rest:Attachments".</summary>
    public string ConfigurationSectionName { get; }
}
