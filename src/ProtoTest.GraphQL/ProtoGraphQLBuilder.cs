namespace ProtoTest.GraphQL;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoTest.Core;
using ProtoTest.Http;

public sealed class ProtoGraphQLBuilder
{
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
        var registeredName = Qualify(name, applicationName);
        _application?.RegisterClient("GraphQL", name);

        return ProtoHttpClientRegistration.AddClient(
            Services,
            "GraphQL",
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
        var registeredName = Qualify(name, applicationName);
        _application?.RegisterClient("GraphQL", name);
        return ProtoHttpClientRegistration.AddClient(
            Services, "GraphQL", registeredName, baseAddressResolver, configure, applicationName);
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
        var registeredName = Qualify(name, applicationName);
        _application?.RegisterClient("GraphQL", name);
        Services.AddSingleton(new ProtoApplicationTarget(registeredName, applicationName ?? registeredName));
        Services.AddSingleton(new ProtoHttpClientAliasRegistration(
            "GraphQL",
            registeredName,
            sourceClientName,
            (context, _) =>
            {
                var baseAddress = context.Client<HttpClient>(sourceClientName).BaseAddress
                    ?? throw new InvalidOperationException($"HTTP client '{sourceClientName}' has no base address.");
                return ValueTask.FromResult(new Uri(baseAddress, endpointPath));
            }));
        return new ProtoHttpTargetBuilder(registeredName, Services);
    }

    public ProtoGraphQLBuilder CaptureAttachments(Action<GraphQLAttachmentOptions>? configure = null)
    {
        Services.RemoveAll<GraphQLAttachmentOptions>();
        Services.AddSingleton(sp =>
        {
            var options = new GraphQLAttachmentOptions();
            configure?.Invoke(options);
            options.BindFromConfiguration(sp.GetRequiredService<IConfiguration>());
            return options;
        });
        return this;
    }

    public ProtoGraphQLBuilder ConfigureResponses(Action<GraphQLResponseOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Services.RemoveAll<GraphQLResponseOptions>();
        Services.AddSingleton(sp =>
        {
            var options = new GraphQLResponseOptions();
            configure(options);
            options.BindFromConfiguration(sp.GetRequiredService<IConfiguration>());
            return options;
        });
        return this;
    }

    private static string Qualify(string name, string? applicationName)
        => applicationName is null ? name : $"{applicationName}:{name}";
}
