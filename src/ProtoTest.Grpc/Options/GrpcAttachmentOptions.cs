namespace ProtoTest.Grpc;

using ProtoTest.Http;

/// <summary>
/// Controls which gRPC request and response messages are automatically attached to a test result.
/// Attachments are serialized to Json with camelCase field names, redacted through the shared
/// <see cref="ProtoHttpAttachmentOptions"/> rules, and capped at
/// <see cref="ProtoTest.Json.JsonDiagnosticOptions.MaxDiagnosticBodyLength"/>. Binds from the
/// inherited <see cref="ProtoHttpAttachmentOptions.ConfigurationSectionName"/>.
/// </summary>
public sealed class GrpcAttachmentOptions : ProtoHttpAttachmentOptions
{
    /// <summary>The configuration section this type binds from.</summary>
    public const string ConfigurationSection = "ProtoTest:Grpc:Attachments";

    public GrpcAttachmentOptions()
        : base(ConfigurationSection)
    {
    }
}
