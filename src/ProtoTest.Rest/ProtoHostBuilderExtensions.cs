namespace ProtoTest.Rest;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using ProtoTest.Core;
using ProtoTest.Rest.Internal;

public sealed class ProtoRestTargetBuilder(string targetName, IServiceCollection services) : IProtoTargetBuilder
{
    public string TargetName { get; } = targetName;
    public IServiceCollection Services { get; } = services;
}

public static class ProtoHostBuilderExtensions
{
    public static IProtoHostBuilder AddRest(this IProtoHostBuilder builder, Action<ProtoRestBuilder>? configure = null)
    {
        builder.AddTestHook<RestLifecycleHook>();

        if (configure != null)
        {
            builder.ConfigureServices(services =>
            {
                var restBuilder = new ProtoRestBuilder(services);
                configure(restBuilder);
            });
        }

        return builder;
    }
}

public sealed class ProtoRestBuilder(IServiceCollection services)
{
    public IServiceCollection Services { get; } = services;

    public IProtoTargetBuilder AddClient(string name = "Default", string? baseUrl = null)
    {
        Services.AddSingleton<IProtoClientInitializer>(sp =>
            new GenericRestClientInitializer(name, baseUrl));

        return new ProtoRestTargetBuilder(name, Services);
    }

    /// <summary>
    /// Enables automatic request, response, and expected-shape test attachments.
    /// </summary>
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
}
