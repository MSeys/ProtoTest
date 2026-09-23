namespace ProtoTest.Http;

using ProtoTest.Core;

/// <summary>
/// The per-call state an HTTP-based protocol response carries: the execution context it was made in,
/// the target and operation identity, attachment naming and the parent operation. One record instead
/// of the long parameter lists every response constructor repeated.
/// </summary>
public sealed record ProtoHttpResponseContext(
    ProtoExecutionContext? Execution = null,
    string? TargetName = null,
    string? Identifier = null,
    ProtoHttpAttachmentOptions? AttachmentOptions = null,
    string? AttachmentPrefix = null,
    string? RequestTraceId = null);
