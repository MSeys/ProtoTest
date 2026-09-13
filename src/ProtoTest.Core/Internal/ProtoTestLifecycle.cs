namespace ProtoTest.Core;

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

        var scope = _rootServiceProvider.CreateScope();
        var testTrace = _trace.StartTest(testName, testId, testMethod);
        var context = new ProtoExecutionContext(testName, scope, testId, testMethod, testTrace);
        var state = new ContextState(
            _host,
            context,
            attributes?.OrderBy(attribute => attribute.Order).ToArray() ?? [],
            attachmentPublisher);
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
        state.SetupOperation = state.Context!.Trace.StartOperation(
            "test.setup", "Setup", "ProtoTest.Core", ProtoTracePhase.Setup,
            new Dictionary<string, string?>
            {
                ["hook.count"] = _hooks.Count.ToString(),
                ["attribute.count"] = state.Attributes.Count.ToString(),
                ["test.class"] = state.Context.TestMethod.DeclaringType?.FullName,
                ["test.method"] = state.Context.TestMethod.Name
            });
        try
        {
            foreach (var hook in _hooks)
            {
                using var operation = state.Context!.Trace.StartOperation(
                    "hook.before",
                    $"Before · {hook.GetType().Name}",
                    "ProtoTest.Core",
                    ProtoTracePhase.Setup,
                    new Dictionary<string, string?>
                    {
                        ["hook.type"] = hook.GetType().FullName,
                        ["hook.order"] = hook.Order.ToString()
                    });
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
                using var operation = state.Context!.Trace.StartOperation(
                    "attribute.before",
                    $"Before · {attribute.GetType().Name}",
                    "ProtoTest.Core",
                    ProtoTracePhase.Setup,
                    new Dictionary<string, string?>
                    {
                        ["attribute.type"] = attribute.GetType().FullName,
                        ["attribute.order"] = attribute.Order.ToString()
                    });
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
            state.ExecutionOperation = state.Context!.Trace.StartOperation(
                "test.execution", "Test execution", "ProtoTest.Core", ProtoTracePhase.Execution,
                new Dictionary<string, string?>
                {
                    ["test.class"] = state.Context.TestMethod.DeclaringType?.FullName,
                    ["test.method"] = state.Context.TestMethod.Name
                });
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

        using var lifecycleOperation = context.Trace.StartOperation(
            isRollback ? "test.rollback" : "test.teardown",
            isRollback ? "Rollback" : "Teardown",
            "ProtoTest.Core",
            isRollback ? ProtoTracePhase.Rollback : ProtoTracePhase.Teardown,
            new Dictionary<string, string?>
            {
                ["hook.completed_count"] = state.CompletedHooks.Count.ToString(),
                ["attribute.completed_count"] = state.CompletedAttributes.Count.ToString(),
                ["attachment.count"] = context.Attachments.Count.ToString(),
                ["test.outcome"] = result.Outcome.ToString()
            });
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
            async () => await context.DisposeAsync(),
            exceptions,
            new Dictionary<string, string?>
            {
                ["context.type"] = context.GetType().FullName,
                ["attachment.count"] = context.Attachments.Count.ToString()
            });

        if (exceptions.Count > exceptionCountBeforeTeardown)
        {
            lifecycleOperation.Fail(exceptions[^1]);
            result = ProtoTestResult.Failed(exceptions[^1]);
        }
        else
        {
            lifecycleOperation.Succeed();
        }
        lifecycleOperation.Dispose();

        if (context.Trace is ProtoTestTraceRecorder recorder)
        {
            await recorder.CaptureArtifactsAsync(context.Attachments);
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
        using var operation = context.Trace.StartOperation(
            kind, name, "ProtoTest.Core", phase, attributes);
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
