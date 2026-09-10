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

    public ProtoTestLifecycle(
        ProtoHost host,
        IServiceProvider rootServiceProvider,
        IEnumerable<IProtoTestHook> hooks,
        IProtoTestIdGenerator testIdGenerator)
    {
        _host = host;
        _rootServiceProvider = rootServiceProvider;
        _hooks = [.. hooks.OrderBy(hook => hook.Order)];
        _testIdGenerator = testIdGenerator;
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
        var context = new ProtoExecutionContext(testName, scope, testId, testMethod);
        var state = new ContextState(
            _host,
            context,
            attributes?.OrderBy(attribute => attribute.Order).ToArray() ?? [],
            attachmentPublisher);
        Current.Value = state;

        return ExecuteBeforeAsync(state);
    }

    public async Task CompleteAsync()
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
        await TeardownAsync(state, exceptions);
        LifecycleExceptionHelper.ThrowIfAny(
            "One or more test lifecycle components failed during teardown.", exceptions);
    }

    private async Task<ProtoExecutionContext> ExecuteBeforeAsync(ContextState state)
    {
        try
        {
            foreach (var hook in _hooks)
            {
                await hook.BeforeTestAsync(state.Context!);
                state.CompletedHooks.Add(hook);
            }

            foreach (var attribute in state.Attributes)
            {
                await attribute.BeforeTestAsync(state.Context!);
                state.CompletedAttributes.Add(attribute);
            }

            return state.Context!;
        }
        catch (Exception exception)
        {
            var exceptions = new List<Exception> { exception };
            await TeardownAsync(state, exceptions);
            LifecycleExceptionHelper.ThrowIfAny(
                "Test setup failed and completed lifecycle components were rolled back.", exceptions);
            throw;
        }
    }

    private static async Task TeardownAsync(ContextState state, List<Exception> exceptions)
    {
        var context = state.Context!;
        foreach (var attribute in state.CompletedAttributes.AsEnumerable().Reverse())
        {
            await LifecycleExceptionHelper.CaptureAsync(
                () => attribute.AfterTestAsync(context), exceptions);
        }

        foreach (var hook in state.CompletedHooks.AsEnumerable().Reverse())
        {
            await LifecycleExceptionHelper.CaptureAsync(
                () => hook.AfterTestAsync(context), exceptions);
        }

        if (state.AttachmentPublisher is not null)
        {
            foreach (var attachment in context.Attachments)
            {
                await LifecycleExceptionHelper.CaptureAsync(
                    async () => await state.AttachmentPublisher.PublishAsync(attachment), exceptions);
            }
        }

        await LifecycleExceptionHelper.CaptureAsync(
            async () => await context.DisposeAsync(), exceptions);

        if (ReferenceEquals(Current.Value, state))
        {
            state.Context = null;
            Current.Value = null;
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
    }
}
