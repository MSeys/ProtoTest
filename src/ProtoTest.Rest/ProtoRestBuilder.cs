namespace ProtoTest.Rest;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Http;

public sealed class ProtoRestBuilder
{
    /// <summary>Stable protocol key REST options are registered and resolved under.</summary>
    public const string ProtocolName = "Rest";

    /// <summary>Configuration section backing <see cref="ConfigureResponses"/>.</summary>
    public const string ResponsesConfigurationSectionName = "ProtoTest:Rest:Responses";

    /// <summary>Configuration section backing <see cref="CaptureAttachments"/>.</summary>
    public const string AttachmentsConfigurationSectionName = "ProtoTest:Rest:Attachments";

    private readonly IProtoApplicationBuilder? _application;

    internal ProtoRestBuilder(IServiceCollection services, IProtoApplicationBuilder? application = null)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        _application = application;
    }

    public IServiceCollection Services { get; }

    /// <summary>
    /// Registers a named REST client. Inside <c>AddApplication</c> the client belongs to that
    /// application and takes its base address from <c>ProtoTest:Applications:{app}</c>, optionally
    /// combined with a named endpoint. The name may be omitted for an application's only REST client.
    /// </summary>
    public IProtoTargetBuilder AddClient(
        string? name = null,
        string? baseUrl = null,
        Action<IHttpClientBuilder>? configure = null,
        string? endpoint = null)
        => ProtoHttpClientRegistration.AddNamedClient(
            Services, ProtocolName, "REST", _application, name, baseUrl, configure, endpoint);

    /// <summary>Adds a client whose absolute base address is resolved from per-test context.</summary>
    public IProtoTargetBuilder AddClient(
        string? name,
        Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>> baseAddressResolver,
        Action<IHttpClientBuilder>? configure = null)
        => ProtoHttpClientRegistration.AddResolvedClient(
            Services, ProtocolName, _application, name, baseAddressResolver, configure);

    /// <summary>Adds a client whose absolute base address is read from per-test context.</summary>
    public IProtoTargetBuilder AddClient(
        string? name,
        Func<ProtoExecutionContext, Uri> baseAddressResolver,
        Action<IHttpClientBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(baseAddressResolver);
        return AddClient(
            name,
            (context, _) => ValueTask.FromResult(baseAddressResolver(context)),
            configure);
    }

    /// <summary>Enables automatic request, response, and expected-shape test attachments.</summary>
    public ProtoRestBuilder CaptureAttachments(Action<ProtoHttpAttachmentOptions>? configure = null)
    {
        ProtoHttpOptionsRegistration.ConfigureAttachmentOptions(
            Services,
            ProtocolName,
            AttachmentsConfigurationSectionName,
            configure);
        return this;
    }

    public ProtoRestBuilder ConfigureResponses(Action<ProtoHttpResponseOptions> configure)
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
