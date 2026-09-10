namespace ProtoTest.Rest;

using Microsoft.Extensions.Configuration;

/// <summary>
/// Controls which REST artifacts are automatically attached to a test result.
/// </summary>
public sealed class RestAttachmentOptions
{
    public const string ConfigurationSectionName = "ProtoTest:Rest:Attachments";

    public bool CaptureRequestBodies { get; set; } = true;
    public bool CaptureResponses { get; set; } = true;
    public bool CaptureExpectedShapes { get; set; } = true;

    /// <summary>Maximum number of characters retained in diagnostic bodies.</summary>
    public int MaxDiagnosticBodyLength { get; set; } = 64 * 1024;

    /// <summary>Whether known sensitive headers and JSON properties are replaced in diagnostics.</summary>
    public bool RedactSensitiveData { get; set; } = true;

    public List<string> SensitiveHeaders { get; set; } =
    [
        "Authorization",
        "Proxy-Authorization",
        "Cookie",
        "Set-Cookie",
        "X-Api-Key"
    ];

    public List<string> SensitiveJsonProperties { get; set; } =
    [
        "password",
        "token",
        "access_token",
        "refresh_token",
        "secret",
        "apiKey",
        "api_key"
    ];

    public List<string> SensitiveQueryParameters { get; set; } =
    [
        "access_token",
        "refresh_token",
        "token",
        "apiKey",
        "api_key",
        "key"
    ];

    internal void Bind(IConfiguration configuration)
        => configuration.GetSection(ConfigurationSectionName).Bind(this);
}
