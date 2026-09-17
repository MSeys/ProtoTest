namespace ProtoTest.GraphQL;

using ProtoTest.Core;
using ProtoTest.Http;

/// <summary>
/// Controls which GraphQL artifacts are automatically attached to a test result.
/// </summary>
public sealed class GraphQLAttachmentOptions : ProtoHttpAttachmentOptions, IProtoConfigurableOptions
{
    public const string ConfigurationSectionName = "ProtoTest:GraphQL:Attachments";

    string IProtoConfigurableOptions.ConfigurationSectionName => ConfigurationSectionName;
}
