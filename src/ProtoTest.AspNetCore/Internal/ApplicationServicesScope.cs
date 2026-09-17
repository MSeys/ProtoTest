namespace ProtoTest.AspNetCore.Internal;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

/// <summary>
/// One DI scope over the application under test, created per test and owned as a resource. Scoped
/// domain services - repositories, command handlers, a <c>DbContext</c> - resolve from here, and are
/// disposed when the test ends rather than leaking into the application's root container.
/// </summary>
internal sealed class ApplicationServicesScope<TProgram> : IDisposable
    where TProgram : class
{
    private readonly IServiceScope _scope;
    private bool _disposed;

    private ApplicationServicesScope(IServiceScope scope) => _scope = scope;

    public IServiceProvider Services => _scope.ServiceProvider;

    public static ApplicationServicesScope<TProgram> Create(ProtoExecutionContext context, string name)
    {
        ArgumentNullException.ThrowIfNull(context);
        var scope = context.ServerFactory<TProgram>(name).Services.CreateScope();
        var services = new ApplicationServicesScope<TProgram>(scope);
        context.RegisterResource(new ProtoResource(
            $"application:services:{name}",
            "application",
            $"{typeof(TProgram).Name} services · {name}",
            release =>
            {
                services.Dispose();
                return ValueTask.CompletedTask;
            }));
        return services;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _scope.Dispose();
    }
}
