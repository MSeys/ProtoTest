namespace ProtoTest.Grpc;

using ProtoTest.Json;

/// <summary>
/// Controls which gRPC request and response messages are automatically attached to a test result.
/// Attachments are serialized to Json with camelCase field names, redacted through the shared
/// <see cref="ProtoDiagnosticCaptureOptions"/> rules, and capped at
/// <see cref="JsonDiagnosticOptions.MaxDiagnosticBodyLength"/>.
/// </summary>
public sealed class GrpcAttachmentOptions : ProtoDiagnosticCaptureOptions
{
    /// <summary>
    /// The section this type binds from. It is not named <c>ConfigurationSectionName</c> because the
    /// inherited instance property already carries that name; the instance property returns this value.
    /// </summary>
    public const string SectionName = "ProtoTest:Grpc:Attachments";

    /// <summary>The pre-1.1 name of <see cref="SectionName"/>.</summary>
    [Obsolete("Use the inherited ConfigurationSectionName instance property, or SectionName.")]
    public const string ConfigurationSection = SectionName;

    public GrpcAttachmentOptions()
        : base(SectionName)
    {
    }
}
