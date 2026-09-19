namespace ProtoTest.Web;

using ProtoTest.Core;
using ProtoTest.Web.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public static class ProtoHostBuilderExtensions
{
    public static IProtoHostBuilder AddWebMiddleware<TMiddleware>(this IProtoHostBuilder builder)
        where TMiddleware : class, IWebOperationMiddleware
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.ConfigureServices(services =>
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IWebOperationMiddleware, TMiddleware>()));
    }

    public static IProtoHostBuilder AddWebWait<TCondition>(
        this IProtoHostBuilder builder,
        WebWaitTiming timing,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        params WebOperationKind[] operations)
        where TCondition : class, IWebWaitCondition
    {
        ArgumentNullException.ThrowIfNull(builder);
        var waitTimeout = timeout ?? TimeSpan.FromSeconds(5);
        var waitPollInterval = pollInterval ?? TimeSpan.FromMilliseconds(50);
        if (waitTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (waitPollInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(pollInterval));
        var selectedOperations = operations.Length == 0
            ? new HashSet<WebOperationKind>([WebOperationKind.Navigate, WebOperationKind.Click, WebOperationKind.Fill])
            : new HashSet<WebOperationKind>(operations);

        return builder.ConfigureServices(services =>
        {
            services.TryAddScoped<TCondition>();
            services.AddSingleton(new WebWaitRegistration(
                typeof(TCondition), timing, selectedOperations, waitTimeout, waitPollInterval));
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IWebOperationMiddleware, WebSynchronizationMiddleware>());
        });
    }

    /// <summary>
    /// Registers the web backend for the host. Sessions are not declared here; a test creates them on
    /// demand by name via <c>Proto.Context.Web(name)</c>, so the sessions are per-test rather than per-run.
    /// </summary>
    public static IProtoHostBuilder AddWebBackend(
        this IProtoHostBuilder builder,
        IWebBackendFactory factory)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);
        return builder.ConfigureServices(services => services.AddWebBackend(factory));
    }

    /// <summary>Registers the web backend services. Shared by the host and application registration paths.</summary>
    public static IServiceCollection AddWebBackend(
        this IServiceCollection services,
        IWebBackendFactory factory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);
        services.TryAddScoped<WebSessionRegistry>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtoTestHook, WebLifecycleHook>());
        services.TryAddSingleton<IWebBackendFactory>(factory);
        // Every backend gets page coverage: Playwright and Selenium both register through this method.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtoCollector, WebCoverageCollector>());
        return services;
    }
}
