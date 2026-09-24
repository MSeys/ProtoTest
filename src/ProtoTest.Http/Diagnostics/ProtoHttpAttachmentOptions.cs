namespace ProtoTest.Http;

using ProtoTest.Core;
using ProtoTest.Json;

/// <summary>
/// Options for capturing and redacting HTTP request/response artifacts, shared by the REST and GraphQL
/// integrations so both observe the same capture and redaction rules. Each protocol registers an
/// instance created with its own configuration section.
/// </summary>
public class ProtoHttpAttachmentOptions : ProtoDiagnosticCaptureOptions
{
    /// <summary>Creates options for a protocol that does not name a section, defaulting to <c>ProtoTest:Http:Attachments</c>.</summary>
    public ProtoHttpAttachmentOptions()
        : this("ProtoTest:Http:Attachments")
    {
    }

    /// <summary>Creates options bound to a known configuration section, e.g. <c>ProtoTest:Rest:Attachments</c>.</summary>
    public ProtoHttpAttachmentOptions(string configurationSectionName)
        : base(configurationSectionName)
    {
    }

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
}
