namespace ProtoTest.GraphQL;

using Microsoft.Extensions.Configuration;
using ProtoTest.Http;
using ProtoTest.Json;

public sealed class GraphQLAttachmentOptions : JsonDiagnosticOptions
{
    public const string ConfigurationSectionName = "ProtoTest:GraphQL:Attachments";
    public bool CaptureRequestBodies { get; set; } = true;
    public bool CaptureResponses { get; set; } = true;
    public bool CaptureExpectedShapes { get; set; } = true;
    internal void Bind(IConfiguration configuration) => configuration.GetSection(ConfigurationSectionName).Bind(this);
}

public sealed class GraphQLResponseOptions : ProtoHttpResponseOptions
{
    public const string ConfigurationSectionName = "ProtoTest:GraphQL:Responses";
    internal void Bind(IConfiguration configuration) => configuration.GetSection(ConfigurationSectionName).Bind(this);
}
