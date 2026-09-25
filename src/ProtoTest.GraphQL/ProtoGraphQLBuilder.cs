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

    /// <summary>
    /// The observation kind a failed request records. It is deliberately not the response kind: a call
    /// that failed was attempted, not covered, so the coverage collector ignores it.
    /// </summary>
    internal const string FailureObservationKind = "graphql.failure";

    /// <summary>The observation kind a successful shape assertion records; schema coverage consumes
    /// the <c>graphql.response</c> kind instead, so this is trace evidence, not coverage.</summary>
    internal const string ShapeObservationKind = "graphql.contract.shape";

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
