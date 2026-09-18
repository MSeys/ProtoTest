namespace ProtoTest.Core;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core.Internal;
using System.Diagnostics;
using System.Reflection;

/// <summary>
/// Owns the ProtoTest service provider and exposes the public run and test lifecycle API.
/// Lifecycle execution is delegated to focused internal components.
/// </summary>
public sealed class ProtoHost : IAsyncDisposable
{
    private readonly IServiceProvider _rootServiceProvider;
    private readonly ProtoRunLifecycle _runLifecycle;
    private readonly ProtoTestLifecycle _testLifecycle;
    private readonly ProtoTraceSession _trace;

    public ProtoHost(IServiceProvider rootServiceProvider)
    {
        _rootServiceProvider = rootServiceProvider ?? throw new ArgumentNullException(nameof(rootServiceProvider));

        var testHooks = _rootServiceProvider.GetServices<IProtoTestHook>().ToArray();
        var runHooks = _rootServiceProvider.GetServices<IProtoRunHook>().ToArray();
        var testIdGenerator = _rootServiceProvider.GetService<IProtoTestIdGenerator>()
            ?? new NumericProtoTestIdGenerator();
        _trace = _rootServiceProvider.GetService<ProtoTraceSession>() ?? new ProtoTraceSession();

        _runLifecycle = new ProtoRunLifecycle(runHooks);
        _testLifecycle = new ProtoTestLifecycle(this, _rootServiceProvider, testHooks, testIdGenerator, _trace);
        ProtoHostRegistry.Register(this);
    }

    /// <summary>
    /// Gets the current test execution context for this asynchronous control flow.
    /// </summary>
    public static ProtoExecutionContext CurrentContext => ProtoTestLifecycle.CurrentContext;

    /// <summary>
    /// Gets the current test execution context, or <see langword="null"/> when none is active on this flow -
    /// the safe lookup for telemetry callbacks, which can run outside the test's context.
    /// </summary>
    public static ProtoExecutionContext? CurrentContextOrNull => ProtoTestLifecycle.TryGetCurrentContext;

    /// <summary>
    /// Gets the host owning the current test, or the sole active host outside a test.
    /// </summary>
    public static ProtoHost CurrentHost => ProtoHostRegistry.GetCurrent(ProtoTestLifecycle.CurrentHost);

    /// <summary>
    /// Finds the trace writer of the test an application span belongs to, matched by its W3C trace id.
    /// This is the safe lookup for application telemetry callbacks, which run outside the test's flow:
    /// <see cref="CurrentContextOrNull"/> is empty there and <see cref="CurrentHost"/> cannot be resolved.
    /// </summary>
    public static IProtoTraceWriter? FindTraceWriter(ActivityTraceId traceId)
        => ProtoHostRegistry.FindTraceWriter(traceId);

    /// <summary>
    /// Returns whether the host is composed with a capability of the given kind (optionally a specific
    /// name). Integrations declare capabilities when they are configured, so this answers what the host
    /// can actually do rather than what it was asked to do.
    /// </summary>
    public bool HasCapability(string kind, string? name = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        return _rootServiceProvider.GetServices<ProtoCapabilityDescriptor>().Any(capability =>
            string.Equals(capability.Kind, kind, StringComparison.Ordinal)
            && (name is null || string.Equals(capability.Name, name, StringComparison.Ordinal)));
    }

    public IConfiguration Configuration => _rootServiceProvider.GetRequiredService<IConfiguration>();

    /// <summary>Gets immutable snapshots of the current run trace.</summary>
    public IProtoTraceSource Trace => _trace;

    /// <summary>
    /// Executes all suite-level BeforeRun hooks in ascending order, then records the capabilities the
    /// host is composed of as run entities.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _runLifecycle.StartAsync(cancellationToken);
        foreach (var capability in _rootServiceProvider.GetServices<ProtoCapabilityDescriptor>())
        {
            _trace.RunWriter.SetEntityState(
                ProtoTraceEntityKinds.Capability,
                $"{capability.Kind}:{capability.Name}",
                capability.Name,
                new Dictionary<string, string?>
                {
                    ["capability.name"] = capability.Name,
                    ["capability.kind"] = capability.Kind,
                    ["capability.source"] = capability.Source
                },
                scope: "run",
                change: "activated");
        }

        _trace.StartListening();
    }

    /// <summary>
    /// Executes all suite-level AfterRun hooks in descending order.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _runLifecycle.StopAsync(cancellationToken);
        }
        finally
        {
            _trace.CompleteRun();
        }
    }

    /// <summary>
    /// Starts a test lifecycle with an explicit numeric ID.
    /// </summary>
    public Task<ProtoExecutionContext> StartTestAsync(
        string testName,
        string testId,
        MethodInfo testMethod,
        IEnumerable<ProtoAttribute>? attributes = null,
        IProtoTestAttachmentPublisher? attachmentPublisher = null)
    {
        _runLifecycle.EnsureTestCanStart();
        return _testLifecycle.StartAsync(
            testName, ProtoTestId.Parse(testId), testMethod, attributes, attachmentPublisher);
    }

    /// <summary>
    /// Starts a test lifecycle with an ID generated by this host.
    /// </summary>
    public Task<ProtoExecutionContext> StartTestAsync(
        string testName,
        MethodInfo testMethod,
        IEnumerable<ProtoAttribute>? attributes = null,
        IProtoTestAttachmentPublisher? attachmentPublisher = null)
    {
        _runLifecycle.EnsureTestCanStart();
        return _testLifecycle.StartAsync(testName, testMethod, attributes, attachmentPublisher);
    }

    /// <summary>
    /// Completes the active test using the exact lifecycle components that completed setup.
    /// </summary>
    public Task CompleteTestAsync() => _testLifecycle.CompleteAsync(ProtoTestResult.Unknown);

    /// <summary>Completes the active test and records the result reported by its framework adapter.</summary>
    public Task CompleteTestAsync(ProtoTestResult result)
        => _testLifecycle.CompleteAsync(result ?? throw new ArgumentNullException(nameof(result)));

    public async ValueTask DisposeAsync()
    {
        var exceptions = new List<Exception>();

        // Completing the run here means `await using var host = ...` alone still runs AfterRun
        // hooks (report sinks, trace export). StopAsync is idempotent, so an explicit stop first
        // makes this a no-op.
        if (_runLifecycle.IsStarted)
        {
            try
            {
                await _runLifecycle.StopAsync(default);
            }
            catch (Exception exception)
            {
                exceptions.Add(exception);
            }
        }

        if (!_runLifecycle.TryMarkDisposed())
        {
            LifecycleExceptionHelper.ThrowIfAny(
                "One or more run hooks failed during disposal.", exceptions);
            return;
        }

        // Run-scoped resources outlive the run itself, so they are released once the reports are
        // written and before the provider they may depend on is disposed.
        try
        {
            if (_rootServiceProvider.GetService<ProtoRunResourceStore>() is { } runResources)
            {
                exceptions.AddRange(await runResources.ReleaseAllAsync(_trace.RunWriter));
            }
        }
        catch (Exception exception)
        {
            exceptions.Add(exception);
        }

        try
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
        catch (Exception exception)
        {
            exceptions.Add(exception);
        }
        finally
        {
            _trace.StopListening();
            _trace.CompleteRun();
            ProtoHostRegistry.Unregister(this);
        }

        LifecycleExceptionHelper.ThrowIfAny(
            "One or more resources failed to dispose.", exceptions);
    }
}
