namespace ProtoTest.Messaging;

using ProtoTest.Core;
using ProtoTest.Json;

/// <summary>
/// Controls whether published and received message payloads are captured as test attachments.
/// Payloads are redacted through <see cref="JsonDiagnosticSanitizer"/> and capped at
/// <see cref="JsonDiagnosticOptions.MaxDiagnosticBodyLength"/>. Non-Json payloads are captured as
/// plain text. Binds from <see cref="ConfigurationSectionName"/>.
/// </summary>
public sealed class MessagingAttachmentOptions : JsonDiagnosticOptions, IProtoConfigurableOptions
{
    /// <summary>The configuration section this type binds from.</summary>
    public const string ConfigurationSectionName = "ProtoTest:Messaging:Attachments";

    string IProtoConfigurableOptions.ConfigurationSectionName => ConfigurationSectionName;

    /// <summary>Captures published payloads as attachments. Defaults to <see langword="true"/>.</summary>
    public bool CapturePublishedPayloads { get; set; } = true;

    /// <summary>Captures received payloads as attachments. Defaults to <see langword="true"/>.</summary>
    public bool CaptureReceivedPayloads { get; set; } = true;
}
