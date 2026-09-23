namespace ProtoTest.Http;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

/// <summary>
/// Shared fluent surface for the HTTP-based protocol builders: named and resolver-based clients,
/// response options and attachment capture. A protocol supplies its key, its configuration sections and
/// the label used when a client is registered under an application.
/// </summary>
public abstract class ProtoHttpProtocolBuilder<TBuilder>
    where TBuilder : ProtoHttpProtocolBuilder<TBuilder>
{
    private readonly string _protocolName;
    private readonly string _applicationClientLabel;
    private readonly string _responsesConfigurationSectionName;
    private readonly string _attachmentsConfigurationSectionName;
    private readonly IProtoApplicationBuilder? _application;

    protected ProtoHttpProtocolBuilder(
        IServiceCollection services,
        string protocolName,
        string applicationClientLabel,
        string responsesConfigurationSectionName,
        string attachmentsConfigurationSectionName,
        IProtoApplicationBuilder? application = null)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        _protocolName = string.IsNullOrWhiteSpace(protocolName)
            ? throw new ArgumentException("A protocol name is required.", nameof(protocolName))
            : protocolName;
        _applicationClientLabel = applicationClientLabel;
        _responsesConfigurationSectionName = responsesConfigurationSectionName;
        _attachmentsConfigurationSectionName = attachmentsConfigurationSectionName;
        _application = application;
    }

    public IServiceCollection Services { get; }

    /// <summary>
    /// Registers a named client. Inside <c>AddApplication</c> the client belongs to that application and
    /// takes its address from <c>ProtoTest:Applications:{app}</c>, optionally combined with a named
    /// endpoint. The name may be omitted for an application's only client.
    /// </summary>
    public IProtoTargetBuilder AddClient(
        string? name = null,
        string? baseUrl = null,
        Action<IHttpClientBuilder>? configure = null,
        string? endpoint = null)
        => ProtoHttpClientRegistration.AddNamedClient(
            Services, _protocolName, _applicationClientLabel, _application, name, baseUrl, configure, endpoint);

    /// <summary>Adds a client whose absolute base address is resolved from per-test context.</summary>
    public IProtoTargetBuilder AddClient(
        string? name,
        Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>> baseAddressResolver,
        Action<IHttpClientBuilder>? configure = null)
        => ProtoHttpClientRegistration.AddResolvedClient(
            Services, _protocolName, _application, name, baseAddressResolver, configure);

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
    public TBuilder CaptureAttachments(Action<ProtoHttpAttachmentOptions>? configure = null)
    {
        ProtoHttpOptionsRegistration.ConfigureAttachmentOptions(
            Services,
            _protocolName,
            _attachmentsConfigurationSectionName,
            configure);
        return (TBuilder)this;
    }

    /// <summary>Tunes response buffering and status diagnostics for this protocol.</summary>
    public TBuilder ConfigureResponses(Action<ProtoHttpResponseOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        ProtoHttpOptionsRegistration.ConfigureResponseOptions(
            Services,
            _protocolName,
            _responsesConfigurationSectionName,
            configure);
        return (TBuilder)this;
    }
}
