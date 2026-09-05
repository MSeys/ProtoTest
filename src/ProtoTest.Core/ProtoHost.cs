namespace ProtoTest.Core;

using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

/// <summary>
/// The central execution engine managing test scope creation, lifecycle hooks, and ambient test context.
/// </summary>
public sealed class ProtoHost(IServiceProvider rootServiceProvider, IEnumerable<IProtoHook>? hooks = null) : IAsyncDisposable
{
    private static readonly AsyncLocal<ProtoExecutionContext?> _currentContext = new();
    private readonly IServiceProvider _rootServiceProvider = rootServiceProvider ?? throw new ArgumentNullException(nameof(rootServiceProvider));
    private readonly IEnumerable<IProtoHook> _hooks = hooks ?? [];

    /// <summary>
    /// Gets the current ambient <see cref="ProtoExecutionContext"/> for the executing thread.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when accessed outside of an active test execution context.</exception>
    public static ProtoExecutionContext Current => _currentContext.Value
        ?? throw new InvalidOperationException("No active ProtoExecutionContext available on this thread.");

    /// <summary>
    /// Binds a new test execution context synchronously to the caller's execution frame and creates a dedicated service scope.
    /// MUST be called synchronously on the runner thread before executing hooks or test logic.
    /// </summary>
    /// <param name="testName">The display name of the executing test.</param>
    /// <param name="testId">The unique identifier for the test run.</param>
    /// <returns>The newly created <see cref="ProtoExecutionContext"/>.</returns>
    public ProtoExecutionContext BeginTestContext(string testName, string testId, MethodInfo testMethod)
    {
        var scope = _rootServiceProvider.CreateScope();
        var context = new ProtoExecutionContext(testName, scope, testId, testMethod);

        // Synchronously bind context to the caller's execution frame
        _currentContext.Value = context;

        return context;
    }

    /// <summary>
    /// Asynchronously executes all pre-test hooks and attributes in ascending order.
    /// </summary>
    /// <param name="attributes">Optional lifecycle attributes attached to the test or fixture.</param>
    public async Task ExecuteBeforeHooksAsync(IEnumerable<ProtoAttribute>? attributes = null)
    {
        var context = Current;

        // 1. Global Hooks: BeforeTestAsync
        foreach (var hook in _hooks.OrderBy(x => x.Order))
        {
            await hook.BeforeTestAsync(context);
        }

        // 2. Method Attributes: BeforeTestAsync
        if (attributes != null)
        {
            foreach (var attribute in attributes.OrderBy(x => x.Order))
            {
                await attribute.BeforeTestAsync(context);
            }
        }
    }

    /// <summary>
    /// Asynchronously executes all post-test hooks and attributes in descending order, then asynchronously disposes the context.
    /// </summary>
    /// <param name="attributes">Optional lifecycle attributes attached to the test or fixture.</param>
    public async Task ExecuteAfterHooksAsync(IEnumerable<ProtoAttribute>? attributes = null)
    {
        var context = _currentContext.Value;
        if (context == null) return;

        try
        {
            // 1. Method Attributes: AfterTestAsync
            if (attributes != null)
            {
                foreach (var attribute in attributes.OrderByDescending(x => x.Order))
                {
                    await attribute.AfterTestAsync(context);
                }
            }

            // 2. Global Hooks: AfterTestAsync
            foreach (var hook in _hooks.OrderByDescending(x => x.Order))
            {
                await hook.AfterTestAsync(context);
            }
        }
        finally
        {
            // Asynchronously dispose context resources while still inside async scope
            await context.DisposeAsync();
        }
    }

    /// <summary>
    /// Synchronously unbinds the test execution context from the caller's execution frame.
    /// MUST be called synchronously on the runner thread after all execution and after-hooks have finished.
    /// </summary>
    public void EndTestContext()
    {
        // Synchronously unbind context from the caller's execution frame
        _currentContext.Value = null;
    }

    /// <summary>
    /// Disposes the underlying root service provider asynchronously.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_rootServiceProvider is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else if (_rootServiceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}