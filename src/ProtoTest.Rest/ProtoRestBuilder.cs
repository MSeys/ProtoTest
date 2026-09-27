namespace ProtoTest.Rest;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Http;

public sealed class ProtoRestBuilder : ProtoHttpProtocolBuilder<ProtoRestBuilder>
{
    /// <summary>Stable protocol key REST options are registered and resolved under.</summary>
    public const string ProtocolName = "Rest";

    /// <summary>Configuration section backing <see cref="ProtoHttpProtocolBuilder{TBuilder}.ConfigureResponses"/>.</summary>
    public const string ResponsesConfigurationSectionName = "ProtoTest:Rest:Responses";

    /// <summary>Configuration section backing <see cref="ProtoHttpProtocolBuilder{TBuilder}.CaptureAttachments"/>.</summary>
    public const string AttachmentsConfigurationSectionName = "ProtoTest:Rest:Attachments";

    /// <summary>The observation kind a request records; matched fakes reuse it with a REST payload.</summary>
    public const string ResponseObservationKind = "http.response";

    /// <summary>The observation kind a failed request records; the coverage collector ignores it.</summary>
    public const string FailureObservationKind = "http.failure";

    /// <summary>The observation kind a successful shape assertion records; OpenAPI coverage consumes it.</summary>
    internal const string ShapeObservationKind = "http.contract.shape";

    /// <summary>The protocol's identity: names, trace source, observation kind and coverage category.</summary>
    internal static readonly ProtoProtocol Protocol = new(
        ProtocolName, "REST", "ProtoTest.Rest", "http.response", "REST");

    internal ProtoRestBuilder(IServiceCollection services, IProtoApplicationBuilder? application = null)
        : base(
            services,
            ProtocolName,
            "REST",
            ResponsesConfigurationSectionName,
            AttachmentsConfigurationSectionName,
            application)
    {
    }
}
