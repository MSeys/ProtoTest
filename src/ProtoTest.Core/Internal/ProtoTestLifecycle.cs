namespace ProtoTest.Core.Internal;

using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

internal sealed class ProtoTestLifecycle
{
    private static readonly AsyncLocal<ContextState?> Current = new();
    private readonly ProtoHost _host;
    private readonly IServiceProvider _rootServiceProvider;
    private readonly IReadOnlyList<IProtoTestHook> _hooks;
    private readonly IProtoTestIdGenerator _testIdGenerator;
    private readonly ProtoTraceSession _trace;

    public ProtoTestLifecycle(
        ProtoHost host,
        IServiceProvider rootServiceProvider,
        IEnumerable<IProtoTestHook> hooks,
        IProtoTestIdGenerator testIdGenerator,
        ProtoTraceSession trace)
    {
        _host = host;
        _rootServiceProvider = rootServiceProvider;
        _hooks = [.. hooks.OrderBy(hook => hook.Order)];
        _testIdGenerator = testIdGenerator;
        _trace = trace;
    }

    public static ProtoExecutionContext CurrentContext => Current.Value?.Context
        ?? throw new InvalidOperationException("No active ProtoExecutionContext available on this thread.");

    /// <summary>Gets the current test context, or <see langword="null"/> when none is active on this flow.</summary>
    public static ProtoExecutionContext? TryGetCurrentContext => Current.Value?.Context;

    public static ProtoHost? CurrentHost => Current.Value?.Host;

    public Task<ProtoExecutionContext> StartAsync(
        string testName,
        MethodInfo testMethod,
        IEnumerable<ProtoAttribute>? attributes,
        IProtoTestAttachmentPublisher? attachmentPublisher)
    {
        ArgumentNullException.ThrowIfNull(testMethod);
        return StartAsync(testName, _testIdGenerator.Next(testMethod), testMethod, attributes, attachmentPublisher);
    }

    public Task<ProtoExecutionContext> StartAsync(
        string testName,
        ProtoTestId testId,
        MethodInfo testMethod,
        IEnumerable<ProtoAttribute>? attributes,
        IProtoTestAttachmentPublisher? attachmentPublisher)
    {
        if (Current.Value?.Context is not null)
        {
            throw new InvalidOperationException(
                "A ProtoExecutionContext is already active on this async flow. Complete the active test before starting another one.");
        }

        ArgumentNullException.ThrowIfNull(testName);
        ArgumentNullException.ThrowIfNull(testMethod);

        // This method stays synchronous so the ambient AsyncLocal context it sets is visible to the
        // caller's execution context; awaiting here would scope the change to this state machine.
        var scope = _rootServiceProvider.CreateScope();
        ContextState state;
        try
        {
            var testTrace = _trace.StartTest(testName, testId, testMethod);
            var context = new ProtoExecutionContext(testName, scope, testId, testMethod, testTrace);
            state = new ContextState(
                _host,
                context,
                attributes?.OrderBy(attribute => attribute.Order).ToArray() ?? [],
                attachmentPublisher);
        }
        catch
        {
            // Execution has not begun, so no teardown will dispose the scope: release it here.
            if (scope is IAsyncDisposable asyncDisposable)
            {
                asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            else
            {
                scope.Dispose();
            }

            throw;
        }

        Current.Value = state;
        return ExecuteBeforeAsync(state);
    }

    public async Task CompleteAsync(ProtoTestResult result)
    {
        var state = Current.Value;
        if (state?.Context is null)
        {
            return;
        }

        if (!ReferenceEquals(state.Host, _host))
        {
            throw new InvalidOperationException("The active test context belongs to a different ProtoHost.");
        }

        var exceptions = new List<Exception>();
        await TeardownAsync(state, exceptions, result, isRollback: false);
        LifecycleExceptionHelper.ThrowIfAny(
            "One or more test lifecycle components failed during teardown.", exceptions);
    }

    private async Task<ProtoExecutionContext> ExecuteBeforeAsync(ContextState state)
    {
        state.SetupOperation = state.Context!.Trace
            .Operation("test.setup", "Setup", "ProtoTest.Core")
            .During(ProtoTracePhase.Setup)
            .With("hook.count", _hooks.Count.ToString())
            .With("attribute.count", state.Attributes.Count.ToString())
            .With("test.class", state.Context.TestMethod.DeclaringType?.FullName)
            .With("test.method", state.Context.TestMethod.Name)
            .Begin();
        try
        {
            foreach (var hook in _hooks)
            {
                using var operation = state.Context!.Trace
                    .Operation("hook.before", $"Before · {hook.GetType().Name}", "ProtoTest.Core")
                    .During(ProtoTracePhase.Setup)
                    .With("hook.type", hook.GetType().FullName)
                    .With("hook.order", hook.Order.ToString())
                    .Begin();
                try
                {
                    await hook.BeforeTestAsync(state.Context);
                    state.CompletedHooks.Add(hook);
                    operation.Succeed();
                }
                catch (Exception exception)
                {
                    operation.Fail(exception);
                    throw;
                }
            }

            foreach (var attribute in state.Attributes)
            {
                using var operation = state.Context!.Trace
                    .Operation("attribute.before", $"Before · {attribute.GetType().Name}", "ProtoTest.Core")
                    .During(ProtoTracePhase.Setup)
                    .With("attribute.type", attribute.GetType().FullName)
                    .With("attribute.order", attribute.Order.ToString())
                    .Begin();
                try
                {
                    await attribute.BeforeTestAsync(state.Context);
                    state.CompletedAttributes.Add(attribute);
                    operation.Succeed();
                }
                catch (Exception exception)
                {
                    operation.Fail(exception);
                    throw;
                }
            }

            state.SetupOperation.Succeed();
            state.SetupOperation.Dispose();
            state.ExecutionOperation = state.Context!.Trace
                .Operation("test.execution", "Test execution", "ProtoTest.Core")
                .During(ProtoTracePhase.Execution)
                .With("test.class", state.Context.TestMethod.DeclaringType?.FullName)
                .With("test.method", state.Context.TestMethod.Name)
                .Begin();
            if (state.Context.Trace is ProtoTestTraceRecorder recorder)
            {
                recorder.SetDefaultParent(state.ExecutionOperation.Id);
            }
            return state.Context!;
        }
        catch (Exception exception)
        {
            state.SetupOperation?.Fail(exception);
            state.SetupOperation?.Dispose();
            var exceptions = new List<Exception> { exception };
            await TeardownAsync(
                state,
                exceptions,
                ProtoTestResult.Failed(exception),
                isRollback: true);
            LifecycleExceptionHelper.ThrowIfAny(
                "Test setup failed and completed lifecycle components were rolled back.", exceptions);
            throw;
        }
    }

    private static async Task TeardownAsync(
        ContextState state,
        List<Exception> exceptions,
        ProtoTestResult result,
        bool isRollback)
    {
        var context = state.Context!;
        state.ExecutionOperation?.Complete(result);
        state.ExecutionOperation?.Dispose();
        if (context.Trace is ProtoTestTraceRecorder testTrace)
        {
            testTrace.SetDefaultParent(null);
        }

        using var lifecycleOperation = context.Trace
            .Operation(
                isRollback ? "test.rollback" : "test.teardown",
                isRollback ? "Rollback" : "Teardown",
                "ProtoTest.Core")
            .During(isRollback ? ProtoTracePhase.Rollback : ProtoTracePhase.Teardown)
            .With("hook.completed_count", state.CompletedHooks.Count.ToString())
            .With("attribute.completed_count", state.CompletedAttributes.Count.ToString())
            .With("attachment.count", context.Attachments.Count.ToString())
            .With("test.outcome", result.Outcome.ToString())
            .Begin();
        var exceptionCountBeforeTeardown = exceptions.Count;

        foreach (var attribute in state.CompletedAttributes.AsEnumerable().Reverse())
        {
            await TraceCleanupAsync(
                context,
                "attribute.after",
                $"After · {attribute.GetType().Name}",
                isRollback ? ProtoTracePhase.Rollback : ProtoTracePhase.Teardown,
                () => attribute.AfterTestAsync(context),
                exceptions,
                new Dictionary<string, string?>
                {
                    ["attribute.type"] = attribute.GetType().FullName,
                    ["attribute.order"] = attribute.Order.ToString()
                });
        }

        foreach (var hook in state.CompletedHooks.AsEnumerable().Reverse())
        {
            await TraceCleanupAsync(
                context,
                "hook.after",
                $"After · {hook.GetType().Name}",
                isRollback ? ProtoTracePhase.Rollback : ProtoTracePhase.Teardown,
                () => hook.AfterTestAsync(context),
                exceptions,
                new Dictionary<string, string?>
                {
                    ["hook.type"] = hook.GetType().FullName,
                    ["hook.order"] = hook.Order.ToString()
                });
        }

        if (state.AttachmentPublisher is not null)
        {
            foreach (var attachment in context.Attachments)
            {
                await TraceCleanupAsync(
                    context,
                    "attachment.publish",
                    $"Publish · {attachment.Name}",
                    isRollback ? ProtoTracePhase.Rollback : ProtoTracePhase.Teardown,
                    async () => await state.AttachmentPublisher.PublishAsync(attachment),
                    exceptions,
                    new Dictionary<string, string?>
                    {
                        ["attachment.name"] = attachment.Name,
                        ["attachment.media_type"] = attachment.MediaType,
                        ["attachment.description"] = attachment.Description,
                        ["attachment.source"] = attachment.IsFile ? "file" : "memory"
                    });
            }
        }

        await TraceCleanupAsync(
            context,
            "context.dispose",
            "Dispose execution context",
            isRollback ? ProtoTracePhase.Rollback : ProtoTracePhase.Teardown,
            () => context.DisposeAsync(isRollback ? ProtoTracePhase.Rollback : ProtoTracePhase.Teardown).AsTask(),
            exceptions,
            new Dictionary<string, string?>
            {
                ["context.type"] = context.GetType().FullName,
                ["attachment.count"] = context.Attachments.Count.ToString()
            });

        if (exceptions.Count > exceptionCountBeforeTeardown)
        {
            // The teardown exception is evidence, not a replacement for the result the test reported:
            // the original outcome stands, so a failed assertion is not hidden by a cleanup error. The
            // failing teardown operation and the findings below carry the teardown error instead.
            lifecycleOperation.Fail(exceptions[^1]);
            foreach (var failure in exceptions.Skip(exceptionCountBeforeTeardown))
            {
                context.Trace.Finding(
                    $"Teardown failed: {failure.Message}",
                    ProtoReportStatus.Error.ToString(),
                    "Teardown",
                    targetName: context.TestName,
                    tags: [failure.GetType().Name]);
            }
        }
        else
        {
            lifecycleOperation.Succeed();
        }
        lifecycleOperation.Dispose();

        if (context.Trace is ProtoTestTraceRecorder recorder)
        {
            try
            {
                await recorder.CaptureArtifactsAsync(context.Attachments);
            }
            catch (Exception exception)
            {
                // Capturing artifacts is one teardown step among several: its failure is reported like
                // any other, but it must not skip completing the test and clearing the ambient context.
                exceptions.Add(exception);
                result = ProtoTestResult.Failed(exception);
            }

            recorder.CompleteTest(result);
        }

        if (ReferenceEquals(Current.Value, state))
        {
            state.Context = null;
            Current.Value = null;
        }
    }

    private static async Task TraceCleanupAsync(
        ProtoExecutionContext context,
        string kind,
        string name,
        ProtoTracePhase phase,
        Func<Task> action,
        List<Exception> exceptions,
        IReadOnlyDictionary<string, string?>? attributes = null)
    {
        using var operation = context.Trace
            .Operation(kind, name, "ProtoTest.Core")
            .During(phase)
            .With(attributes)
            .Begin();
        try
        {
            await action();
            operation.Succeed();
        }
        catch (Exception exception)
        {
            operation.Fail(exception);
            exceptions.Add(exception);
        }
    }

    private sealed class ContextState(
        ProtoHost host,
        ProtoExecutionContext context,
        IReadOnlyList<ProtoAttribute> attributes,
        IProtoTestAttachmentPublisher? attachmentPublisher)
    {
        public ProtoHost Host { get; } = host;
        public ProtoExecutionContext? Context { get; set; } = context;
        public IReadOnlyList<ProtoAttribute> Attributes { get; } = attributes;
        public IProtoTestAttachmentPublisher? AttachmentPublisher { get; } = attachmentPublisher;
        public List<IProtoTestHook> CompletedHooks { get; } = [];
        public List<ProtoAttribute> CompletedAttributes { get; } = [];
        public ProtoTraceOperation? SetupOperation { get; set; }
        public ProtoTraceOperation? ExecutionOperation { get; set; }
    }
}
