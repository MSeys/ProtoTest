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
