namespace ProtoTest.Rest;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoTest.Core;
using ProtoTest.Http;

public sealed class ProtoRestBuilder
{
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
        var registeredName = Qualify(name, applicationName);
        _application?.RegisterClient("Rest", name);

        return ProtoHttpClientRegistration.AddClient(
            Services,
            "Rest",
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
        var registeredName = Qualify(name, applicationName);
        _application?.RegisterClient("Rest", name);
        return ProtoHttpClientRegistration.AddClient(
            Services, "Rest", registeredName, baseAddressResolver, configure, applicationName);
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
        var registeredName = Qualify(name, applicationName);
        _application?.RegisterClient("Rest", name);
        Services.AddSingleton(new ProtoApplicationTarget(registeredName, applicationName ?? registeredName));
        Services.AddSingleton(new ProtoHttpClientAliasRegistration(
            "Rest",
            registeredName,
            sourceClientName,
            (context, _) =>
            {
                var baseAddress = context.Client<HttpClient>(sourceClientName).BaseAddress
                    ?? throw new InvalidOperationException($"HTTP client '{sourceClientName}' has no base address.");
                return ValueTask.FromResult(
                    string.IsNullOrWhiteSpace(basePath) ? baseAddress : new Uri(baseAddress, basePath));
            }));
        return new ProtoHttpTargetBuilder(registeredName, Services);
    }

    /// <summary>Enables automatic request, response, and expected-shape test attachments.</summary>
    public ProtoRestBuilder CaptureAttachments(Action<RestAttachmentOptions>? configure = null)
    {
        Services.RemoveAll<RestAttachmentOptions>();
        Services.AddSingleton(serviceProvider =>
        {
            var options = new RestAttachmentOptions();
            configure?.Invoke(options);
            options.BindFromConfiguration(serviceProvider.GetRequiredService<IConfiguration>());
            return options;
        });
        return this;
    }

    public ProtoRestBuilder ConfigureResponses(Action<RestResponseOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Services.RemoveAll<RestResponseOptions>();
        Services.AddSingleton(serviceProvider =>
        {
            var options = new RestResponseOptions();
            configure(options);
            options.BindFromConfiguration(serviceProvider.GetRequiredService<IConfiguration>());
            return options;
        });
        return this;
    }

    private static string Qualify(string name, string? applicationName)
        => applicationName is null ? name : $"{applicationName}:{name}";
}
