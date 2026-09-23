namespace ProtoTest.GraphQL;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Http;

public sealed class ProtoGraphQLBuilder : ProtoHttpProtocolBuilder<ProtoGraphQLBuilder>
{
    /// <summary>Stable protocol key GraphQL options are registered and resolved under.</summary>
    public const string ProtocolName = "GraphQL";

    /// <summary>Configuration section backing <see cref="ProtoHttpProtocolBuilder{TBuilder}.ConfigureResponses"/>.</summary>
    public const string ResponsesConfigurationSectionName = "ProtoTest:GraphQL:Responses";

    /// <summary>Configuration section backing <see cref="ProtoHttpProtocolBuilder{TBuilder}.CaptureAttachments"/>.</summary>
    public const string AttachmentsConfigurationSectionName = "ProtoTest:GraphQL:Attachments";

    /// <summary>The protocol's identity: names, trace source, observation kind and coverage category.</summary>
    internal static readonly ProtoProtocol Protocol = new(
        ProtocolName, "GraphQL", "ProtoTest.GraphQL", "graphql.response", "GraphQL operation");

    internal ProtoGraphQLBuilder(IServiceCollection services, IProtoApplicationBuilder? application = null)
        : base(
            services,
            ProtocolName,
            "GraphQL",
            ResponsesConfigurationSectionName,
            AttachmentsConfigurationSectionName,
            application)
    {
    }
}
