namespace ProtoTest.Http;

using ProtoTest.Json;

/// <summary>
/// Base options for capturing and redacting HTTP request/response artifacts, shared by the REST and
/// GraphQL integrations so both observe the same capture and redaction rules.
/// </summary>
public class ProtoHttpAttachmentOptions : JsonDiagnosticOptions
{
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
    [
        "access_token",
        "refresh_token",
        "token",
        "apiKey",
        "api_key",
        "key"
    ];
}
