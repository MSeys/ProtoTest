namespace ProtoTest.GraphQL;

using ProtoTest.Core;
using ProtoTest.Json;

public sealed class GraphQLAttachmentOptions : JsonDiagnosticOptions, IProtoConfigurableOptions
{
    public const string ConfigurationSectionName = "ProtoTest:GraphQL:Attachments";
    public bool CaptureRequestBodies { get; set; } = true;
    public bool CaptureResponses { get; set; } = true;
    public bool CaptureExpectedShapes { get; set; } = true;
    string IProtoConfigurableOptions.ConfigurationSectionName => ConfigurationSectionName;
}
