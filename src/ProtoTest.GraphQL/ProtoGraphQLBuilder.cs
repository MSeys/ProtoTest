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
    /// application and takes its endpoint from <c>ProtoTest:Applications:{app}</c>.
    /// </summary>
    public IProtoTargetBuilder AddClient(
        string name = "Default",
        string? baseUrl = null,
        Action<IHttpClientBuilder>? configure = null,
        string? endpoint = "GraphQL")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var applicationName = _application?.ApplicationName;
        var registeredName = ProtoHttpClientRegistration.Qualify(name, applicationName);
        _application?.RegisterClient(ProtocolName, name);

        return ProtoHttpClientRegistration.AddClient(
            Services,
            ProtocolName,
            "GraphQL",
            registeredName,
            baseUrl,
            configure,
            applicationName,
            endpoint,
            allowMissingBaseUrl: applicationName is not null && baseUrl is null);
    }

    public IProtoTargetBuilder AddClient(
        string name,
        Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>> baseAddressResolver,
        Action<IHttpClientBuilder>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var applicationName = _application?.ApplicationName;
        var registeredName = ProtoHttpClientRegistration.Qualify(name, applicationName);
        _application?.RegisterClient(ProtocolName, name);
        return ProtoHttpClientRegistration.AddClient(
            Services, ProtocolName, registeredName, baseAddressResolver, configure, applicationName);
    }

    public IProtoTargetBuilder AddClient(
        string name,
        Func<ProtoExecutionContext, Uri> baseAddressResolver,
        Action<IHttpClientBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(baseAddressResolver);
        return AddClient(name, (context, _) => ValueTask.FromResult(baseAddressResolver(context)), configure);
    }

    /// <summary>
    /// Uses an HTTP client registered by another integration, such as an in-process ASP.NET Core server.
    /// </summary>
    public IProtoTargetBuilder AddClientFrom(
        string name,
        string sourceClientName,
        string endpointPath = "/graphql")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceClientName);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointPath);
        var applicationName = _application?.ApplicationName;
        var registeredName = ProtoHttpClientRegistration.Qualify(name, applicationName);
        _application?.RegisterClient(ProtocolName, name);
        return ProtoHttpClientRegistration.AddClientFrom(
            Services, ProtocolName, registeredName, sourceClientName, endpointPath, applicationName);
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
