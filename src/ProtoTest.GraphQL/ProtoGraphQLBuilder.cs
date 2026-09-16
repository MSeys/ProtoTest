namespace ProtoTest.GraphQL;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoTest.Core;
using ProtoTest.Http;

public sealed class ProtoGraphQLBuilder(IServiceCollection services)
{
    public IServiceCollection Services { get; } = services;

    public IProtoTargetBuilder AddClient(
        string name = "Default",
        string? baseUrl = null,
        Action<IHttpClientBuilder>? configure = null)
        => ProtoHttpClientRegistration.AddClient(Services, "GraphQL", "GraphQL", name, baseUrl, configure);

    public IProtoTargetBuilder AddClient(
        string name,
        Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>> baseAddressResolver,
        Action<IHttpClientBuilder>? configure = null)
        => ProtoHttpClientRegistration.AddClient(Services, "GraphQL", name, baseAddressResolver, configure);

    public IProtoTargetBuilder AddClient(
        string name,
        Func<ProtoExecutionContext, Uri> baseAddressResolver,
        Action<IHttpClientBuilder>? configure = null)
        => AddClient(name, (context, _) => ValueTask.FromResult(baseAddressResolver(context)), configure);

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
        Services.AddSingleton(new ProtoHttpClientAliasRegistration(
            "GraphQL",
            name,
            sourceClientName,
            (context, _) =>
            {
                var baseAddress = context.Client<HttpClient>(sourceClientName).BaseAddress
                    ?? throw new InvalidOperationException($"HTTP client '{sourceClientName}' has no base address.");
                return ValueTask.FromResult(new Uri(baseAddress, endpointPath));
            }));
        return new ProtoHttpTargetBuilder(name, Services);
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
}

public static class ProtoGraphQLTargetBuilderExtensions
{
    public static IProtoTargetBuilder WithSubscriptionTransport(
        this IProtoTargetBuilder target,
        GraphQLSubscriptionTransport transport)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.Services.AddSingleton(new GraphQLSubscriptionTransportRegistration(target.TargetName, transport));
        return target;
    }

    public static IProtoTargetBuilder WithSchemaCoverage(this IProtoTargetBuilder target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.WithCollector<GraphQLSchemaCoverageCollector>();
    }

    public static IProtoTargetBuilder WithSchemaCoverage(this IProtoTargetBuilder target, string schemaSource)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaSource);
        return target.WithCollector<GraphQLSchemaCoverageCollector>(schemaSource);
    }
}
