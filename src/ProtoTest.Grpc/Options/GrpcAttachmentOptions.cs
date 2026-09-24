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
    /// <summary>The configuration section this type binds from.</summary>
    public const string ConfigurationSection = "ProtoTest:Grpc:Attachments";

    public GrpcAttachmentOptions()
        : base(ConfigurationSection)
    {
    }
}
