namespace ProtoTest.Rest;

using ProtoTest.Core;
using ProtoTest.Http;

/// <summary>
/// Controls which REST artifacts are automatically attached to a test result.
/// </summary>
public sealed class RestAttachmentOptions : ProtoHttpAttachmentOptions, IProtoConfigurableOptions
{
    public const string ConfigurationSectionName = "ProtoTest:Rest:Attachments";

    string IProtoConfigurableOptions.ConfigurationSectionName => ConfigurationSectionName;
}
