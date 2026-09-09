namespace ProtoTest.Rest;

using Microsoft.Extensions.Configuration;

/// <summary>
/// Controls which REST artifacts are automatically attached to a test result.
/// </summary>
public sealed class RestAttachmentOptions
{
    public const string ConfigurationSectionName = "ProtoTest:Rest:Attachments";

    private int _sequence;

    public bool CaptureRequestBodies { get; set; } = true;
    public bool CaptureResponses { get; set; } = true;
    public bool CaptureExpectedShapes { get; set; } = true;

    internal int NextAttachmentNumber() => Interlocked.Increment(ref _sequence);

    internal void Bind(IConfiguration configuration)
        => configuration.GetSection(ConfigurationSectionName).Bind(this);
}
