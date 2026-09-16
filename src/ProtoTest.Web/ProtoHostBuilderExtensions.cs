namespace ProtoTest.Web;

using ProtoTest.Core;
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

    /// <summary>Registers one named, test-scoped web session supplied by an adapter.</summary>
    public static IProtoHostBuilder AddWebBackend(
        this IProtoHostBuilder builder,
        IWebBackendFactory factory,
        string name = "Default")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return builder.ConfigureServices(services =>
        {
            services.TryAddScoped<WebSessionRegistry>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtoTestHook, WebLifecycleHook>());
            services.AddSingleton<IProtoClientInitializer>(new WebClientInitializer(name, factory));
        });
    }

    private sealed class WebClientInitializer(string name, IWebBackendFactory factory)
        : IProtoClientInitializer<WebSession>
    {
        public string Name { get; } = name;

        public async Task<bool> TryInitializeAsync(
            ProtoExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = new WebSession(context, factory, Name);
            context.RegisterClient(session, Name);
            context.Service<WebSessionRegistry>().Add(session);
            return true;
        }
    }

    private sealed class WebLifecycleHook : IProtoTestHook
    {
        // Core client initialization is int.MinValue. Web finalization runs after normal
        // user teardown hooks, but before attachment publication and client disposal.
        public int Order => int.MinValue + 1;

        public Task BeforeTestAsync(ProtoExecutionContext context) => Task.CompletedTask;

        public async Task AfterTestAsync(ProtoExecutionContext context)
        {
            foreach (var session in context.Service<WebSessionRegistry>().Sessions.Reverse())
                await session.CompleteAsync();
        }
    }

    private sealed class WebSessionRegistry
    {
        private readonly List<WebSession> _sessions = [];
        public IReadOnlyList<WebSession> Sessions => _sessions;
        public void Add(WebSession session) => _sessions.Add(session);
    }
}
