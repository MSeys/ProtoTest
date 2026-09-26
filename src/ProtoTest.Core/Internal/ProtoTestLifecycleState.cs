namespace ProtoTest.Core.Internal;

/// <summary>
/// The mutable state of one started test: its host and context, the components that completed setup and
/// the trace operations the lifecycle opened. The test lifecycle stores it in <see cref="ProtoAmbient"/>,
/// so the active test is what every ambient lookup reads, and completes it in place -
/// <see cref="Context"/> becomes null on completion, so a flow that captured the state no longer sees an
/// active test.
/// </summary>
internal sealed class ProtoTestLifecycleState(
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
