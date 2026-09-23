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
