namespace ProtoTest.Http;

using ProtoTest.Core;
using ProtoTest.Json;

/// <summary>
/// Base options for capturing and redacting HTTP request/response artifacts, shared by the REST and
/// GraphQL integrations so both observe the same capture and redaction rules. Each protocol
/// registers an instance created with its own configuration section.
/// </summary>
public class ProtoHttpAttachmentOptions : JsonDiagnosticOptions, IProtoConfigurableOptions
{
    /// <summary>Creates options for a protocol that does not name a section, defaulting to <c>ProtoTest:Http:Attachments</c>.</summary>
    public ProtoHttpAttachmentOptions()
        : this("ProtoTest:Http:Attachments")
    {
    }

    /// <summary>Creates options bound to a known configuration section, e.g. <c>ProtoTest:Rest:Attachments</c>.</summary>
    public ProtoHttpAttachmentOptions(string configurationSectionName)
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

    /// <summary>Header names whose values are redacted in diagnostics.</summary>
    public List<string> SensitiveHeaders { get; set; } =
    [
        "Authorization",
        "Proxy-Authorization",
        "Cookie",
        "Set-Cookie",
        "X-Api-Key"
    ];

    /// <summary>Query parameter names whose values are redacted in diagnostics.</summary>
    public List<string> SensitiveQueryParameters { get; set; } =
        [.. ProtoUriSanitizer.DefaultSensitiveQueryParameters];

    /// <summary>The configuration section this instance binds from, e.g. "ProtoTest:Rest:Attachments".</summary>
    public string ConfigurationSectionName { get; }
}
