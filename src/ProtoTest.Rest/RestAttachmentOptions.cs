namespace ProtoTest.Rest;

using ProtoTest.Core;
using ProtoTest.Json;

/// <summary>
/// Controls which REST artifacts are automatically attached to a test result.
/// </summary>
public sealed class RestAttachmentOptions : JsonDiagnosticOptions, IProtoConfigurableOptions
{
    public const string ConfigurationSectionName = "ProtoTest:Rest:Attachments";

    public bool CaptureRequestBodies { get; set; } = true;
    public bool CaptureResponses { get; set; } = true;
    public bool CaptureExpectedShapes { get; set; } = true;

    public List<string> SensitiveHeaders { get; set; } =
    [
        "Authorization",
        "Proxy-Authorization",
        "Cookie",
        "Set-Cookie",
        "X-Api-Key"
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

    string IProtoConfigurableOptions.ConfigurationSectionName => ConfigurationSectionName;
}
