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
    /// combined with the endpoint named after the client.
    /// </summary>
    public IProtoTargetBuilder AddClient(
        string name = "Default",
        string? baseUrl = null,
        Action<IHttpClientBuilder>? configure = null,
        string? endpoint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var applicationName = _application?.ApplicationName;
        var registeredName = ProtoHttpClientRegistration.Qualify(name, applicationName);
        _application?.RegisterClient(ProtocolName, name);

        return ProtoHttpClientRegistration.AddClient(
            Services,
            ProtocolName,
            "REST",
            registeredName,
            baseUrl,
            configure,
            applicationName,
            endpoint ?? (applicationName is null ? null : name),
            // Under an application with no configured URL, the client reuses the application's
            // in-process transport (see AddAspNetCoreServer) or its BaseUrl.
            allowMissingBaseUrl: applicationName is not null && baseUrl is null);
    }

    /// <summary>Adds a client whose absolute base address is resolved from per-test context.</summary>
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

    /// <summary>Adds a client whose absolute base address is read from per-test context.</summary>
    public IProtoTargetBuilder AddClient(
        string name,
        Func<ProtoExecutionContext, Uri> baseAddressResolver,
        Action<IHttpClientBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(baseAddressResolver);
        return AddClient(
            name,
            (context, _) => ValueTask.FromResult(baseAddressResolver(context)),
            configure);
    }

    /// <summary>
    /// Uses the transport of an HTTP client registered by another integration, such as an in-process
    /// ASP.NET Core server, optionally rooted at a path.
    /// </summary>
    public IProtoTargetBuilder AddClientFrom(string name, string sourceClientName, string? basePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceClientName);
        var applicationName = _application?.ApplicationName;
        var registeredName = ProtoHttpClientRegistration.Qualify(name, applicationName);
        _application?.RegisterClient(ProtocolName, name);
        return ProtoHttpClientRegistration.AddClientFrom(
            Services, ProtocolName, registeredName, sourceClientName, basePath, applicationName);
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
