namespace ProtoTest.GraphQL;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Http;

public sealed class ProtoGraphQLBuilder
{
    /// <summary>Stable protocol key GraphQL options are registered and resolved under.</summary>
    public const string ProtocolName = "GraphQL";

    /// <summary>Configuration section backing <see cref="ConfigureResponses"/>.</summary>
    public const string ResponsesConfigurationSectionName = "ProtoTest:GraphQL:Responses";

    /// <summary>Configuration section backing <see cref="CaptureAttachments"/>.</summary>
    public const string AttachmentsConfigurationSectionName = "ProtoTest:GraphQL:Attachments";

    private readonly IProtoApplicationBuilder? _application;

    internal ProtoGraphQLBuilder(IServiceCollection services, IProtoApplicationBuilder? application = null)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        _application = application;
    }

    public IServiceCollection Services { get; }

    /// <summary>
    /// Registers a named GraphQL client. Inside <c>AddApplication</c> the client belongs to that
    /// application and takes its endpoint from <c>ProtoTest:Applications:{app}</c>. The name may be
    /// omitted for an application's only GraphQL client; pass an endpoint key to root it at a path.
    /// </summary>
    public IProtoTargetBuilder AddClient(
        string? name = null,
        string? baseUrl = null,
        Action<IHttpClientBuilder>? configure = null,
        string? endpoint = null)
        => ProtoHttpClientRegistration.AddNamedClient(
            Services, ProtocolName, "GraphQL", _application, name, baseUrl, configure, endpoint);

    public IProtoTargetBuilder AddClient(
        string? name,
        Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>> baseAddressResolver,
        Action<IHttpClientBuilder>? configure = null)
        => ProtoHttpClientRegistration.AddResolvedClient(
            Services, ProtocolName, _application, name, baseAddressResolver, configure);

    public IProtoTargetBuilder AddClient(
        string? name,
        Func<ProtoExecutionContext, Uri> baseAddressResolver,
        Action<IHttpClientBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(baseAddressResolver);
        return AddClient(name, (context, _) => ValueTask.FromResult(baseAddressResolver(context)), configure);
    }

    public ProtoGraphQLBuilder CaptureAttachments(Action<ProtoHttpAttachmentOptions>? configure = null)
    {
        ProtoHttpOptionsRegistration.ConfigureAttachmentOptions(
            Services,
            ProtocolName,
            AttachmentsConfigurationSectionName,
            configure);
        return this;
    }

    public ProtoGraphQLBuilder ConfigureResponses(Action<ProtoHttpResponseOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        ProtoHttpOptionsRegistration.ConfigureResponseOptions(
            Services,
            ProtocolName,
            ResponsesConfigurationSectionName,
            configure);
        return this;
    }
}
