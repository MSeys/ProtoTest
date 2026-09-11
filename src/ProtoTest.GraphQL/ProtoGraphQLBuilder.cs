namespace ProtoTest.GraphQL;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.GraphQL.Internal;

public sealed class ProtoGraphQLBuilder(IServiceCollection services)
{
    public IServiceCollection Services { get; } = services;

    public IProtoTargetBuilder AddClient(
        string name = "Default",
        string? baseUrl = null,
        Action<IHttpClientBuilder>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (baseUrl is not null && !ProtoHttpUri.TryCreateAbsoluteHttpUri(baseUrl, out _))
        {
            throw new ArgumentException("A GraphQL client base URL must be an absolute HTTP or HTTPS URI.", nameof(baseUrl));
        }

        var http = Services.AddHttpClient(ProtoHttpClientInitializer.GetFactoryName("GraphQL", name));
        configure?.Invoke(http);
        Services.AddSingleton<IProtoClientInitializer>(_ => new ProtoHttpClientInitializer("GraphQL", name, baseUrl));
        return new ProtoGraphQLTargetBuilder(name, Services);
    }

    public IProtoTargetBuilder AddClient(
        string name,
        Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>> baseAddressResolver,
        Action<IHttpClientBuilder>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(baseAddressResolver);
        var http = Services.AddHttpClient(ProtoHttpClientInitializer.GetFactoryName("GraphQL", name));
        configure?.Invoke(http);
        Services.AddSingleton<IProtoClientInitializer>(_ => new ProtoHttpClientInitializer("GraphQL", name, allowMissingBaseUrl: true));
        Services.AddSingleton(new ProtoHttpBaseAddressRegistration("GraphQL", name, baseAddressResolver));
        return new ProtoGraphQLTargetBuilder(name, Services);
    }

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
        return new ProtoGraphQLTargetBuilder(name, Services);
    }

    public ProtoGraphQLBuilder CaptureAttachments(Action<GraphQLAttachmentOptions>? configure = null)
    {
        Services.RemoveAll<GraphQLAttachmentOptions>();
        Services.AddSingleton(sp =>
        {
            var options = new GraphQLAttachmentOptions();
            configure?.Invoke(options);
            options.Bind(sp.GetRequiredService<IConfiguration>());
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
            options.Bind(sp.GetRequiredService<IConfiguration>());
            return options;
        });
        return this;
    }
}

internal sealed class ProtoGraphQLTargetBuilder(string targetName, IServiceCollection services)
    : IProtoTargetBuilder
{
    public string TargetName { get; } = targetName;
    public IServiceCollection Services { get; } = services;
}

public static class ProtoGraphQLTargetBuilderExtensions
{
    public static IProtoTargetBuilder WithSchemaCoverage(this IProtoTargetBuilder target)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.Services.AddSingleton<IProtoCollector>(services =>
            ActivatorUtilities.CreateInstance<GraphQLSchemaCoverageCollector>(services, target.TargetName));
        return target;
    }

    public static IProtoTargetBuilder WithSchemaCoverage(this IProtoTargetBuilder target, string schemaSource)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaSource);
        target.Services.AddSingleton<IProtoCollector>(_ =>
            new GraphQLSchemaCoverageCollector(target.TargetName, schemaSource));
        return target;
    }
}
