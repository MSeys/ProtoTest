namespace ProtoTest.Hosting;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProtoTest.Core;
using ProtoTest.Hosting.Internal;

/// <summary>Reaches the worker hosts a run started from inside a test.</summary>
public static class ProtoExecutionContextExtensions
{
    /// <summary>
    /// Gets the running host of the worker built from <typeparamref name="TProgram"/>. Pass
    /// <paramref name="name"/> when several workers host the same program.
    /// </summary>
    public static IHost Host<TProgram>(this ProtoExecutionContext context, string? name = null)
        where TProgram : class
    {
        ArgumentNullException.ThrowIfNull(context);
        return Registry(context).Find<TProgram>(name);
    }

    /// <summary>
    /// Resolves one service from the worker's host container, for example its background service, so a
    /// test can drive or inspect it.
    /// </summary>
    public static TService HostService<TProgram, TService>(this ProtoExecutionContext context, string? name = null)
        where TProgram : class
        where TService : notnull
    {
        ArgumentNullException.ThrowIfNull(context);
        return Registry(context).Find<TProgram>(name).Services.GetRequiredService<TService>();
    }

    private static ProtoWorkerRegistry Registry(ProtoExecutionContext context)
        => context.TryService<ProtoWorkerRegistry>()
            ?? throw new InvalidOperationException(
                "This host was not built with ProtoTest.Hosting; register a worker with builder.AddWorkerHost<TProgram>().");
}
