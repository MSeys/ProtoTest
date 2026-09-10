namespace ProtoTest.Rest;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoTest.Core;
using ProtoTest.Rest.Internal;

public sealed class ProtoRestBuilder
{
    internal ProtoRestBuilder(IServiceCollection services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public IServiceCollection Services { get; }

    public IProtoTargetBuilder AddClient(
        string name = "Default",
        string? baseUrl = null,
        Action<IHttpClientBuilder>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (baseUrl is not null && !RestUriBuilder.TryCreateAbsoluteHttpUri(baseUrl, out _))
        {
            throw new ArgumentException(
                "A REST client base URL must be an absolute HTTP or HTTPS URI.",
                nameof(baseUrl));
        }

        var httpClientBuilder = Services.AddHttpClient(RestHttpClientInitializer.GetFactoryName(name));
        configure?.Invoke(httpClientBuilder);
        Services.AddSingleton<IProtoClientInitializer>(
            _ => new RestHttpClientInitializer(name, baseUrl));

        return new ProtoRestTargetBuilder(name, Services);
    }

    /// <summary>Adds a client whose absolute base address is resolved from per-test context.</summary>
    public IProtoTargetBuilder AddClient(
        string name,
        Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>> baseAddressResolver,
        Action<IHttpClientBuilder>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(baseAddressResolver);

        var httpClientBuilder = Services.AddHttpClient(RestHttpClientInitializer.GetFactoryName(name));
        configure?.Invoke(httpClientBuilder);
        Services.AddSingleton<IProtoClientInitializer>(
            _ => new RestHttpClientInitializer(name, allowMissingBaseUrl: true));
        Services.AddSingleton(new RestBaseAddressRegistration(name, baseAddressResolver));
        return new ProtoRestTargetBuilder(name, Services);
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

    /// <summary>Enables automatic request, response, and expected-shape test attachments.</summary>
    public ProtoRestBuilder CaptureAttachments(Action<RestAttachmentOptions>? configure = null)
    {
        Services.AddSingleton(serviceProvider =>
        {
            var options = new RestAttachmentOptions();
            configure?.Invoke(options);
            options.Bind(serviceProvider.GetRequiredService<IConfiguration>());
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
            options.Bind(serviceProvider.GetRequiredService<IConfiguration>());
            return options;
        });
        return this;
    }
}
