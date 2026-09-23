namespace ProtoTest.Http;

using System.Net;
using System.Text.Json;
using ProtoTest.Core;
using ProtoTest.Json;

/// <summary>
/// The shared half of an HTTP-based protocol response: the raw message and body, the execution context
/// it was made in, the attachment naming state, the status assertion and disposal. A protocol derives
/// from it and adds its own document model - REST adds binary and dynamic content, GraphQL the
/// data/errors envelope - so a new HTTP-based integration starts from the same mechanics and keeps the
/// same diagnostics contract.
/// </summary>
public abstract class ProtoHttpResponse : IDisposable
{
    private int _shapeAssertionSequence;

    protected ProtoHttpResponse(
        HttpResponseMessage rawResponse,
        string content,
        TimeSpan elapsed,
        ProtoExecutionContext? context = null,
        string? targetName = null,
        string? identifier = null,
        ProtoHttpAttachmentOptions? attachmentOptions = null,
        string? attachmentPrefix = null,
        string? requestTraceId = null)
    {
        RawResponse = rawResponse ?? throw new ArgumentNullException(nameof(rawResponse));
        Content = content ?? string.Empty;
        ElapsedTime = elapsed;
        Context = context;
        TargetName = targetName;
        Identifier = identifier;
        AttachmentOptions = attachmentOptions;
        AttachmentPrefix = attachmentPrefix;
        RequestTraceId = requestTraceId;
    }

    /// <summary>The response message this instance owns and disposes.</summary>
    public HttpResponseMessage RawResponse { get; }

    /// <summary>The response status.</summary>
    public HttpStatusCode StatusCode => RawResponse.StatusCode;

    /// <summary>The body as text, exactly as the protocol read it.</summary>
    public string Content { get; }

    public TimeSpan ElapsedTime { get; }

    /// <summary>The test context the call was made in; null for an untraced assertion.</summary>
    protected ProtoExecutionContext? Context { get; }

    /// <summary>The target name the protocol reported the call under, when it has one.</summary>
    protected string? TargetName { get; }

    /// <summary>The operation identifier the protocol records on assertions and observations.</summary>
    protected string? Identifier { get; }

    /// <summary>The redaction and capture rules the protocol resolved, when attachment capture is on.</summary>
    protected ProtoHttpAttachmentOptions? AttachmentOptions { get; }

    /// <summary>The per-call attachment prefix, such as <c>rest-01</c>.</summary>
    protected string? AttachmentPrefix { get; }

    /// <summary>The trace operation that made the call, used as the parent of assertions.</summary>
    protected string? RequestTraceId { get; }

    /// <summary>
    /// The name for the next expected-shape attachment, or null when the protocol did not opt in or the
    /// response was made without a context (an untraced assertion). The name carries a per-response
    /// sequence so repeated shape assertions stay distinct.
    /// </summary>
    protected string? NextExpectedShapeAttachmentName()
    {
        if (Context is null || AttachmentOptions?.CaptureExpectedShapes != true)
        {
            return null;
        }

        var assertionNumber = Interlocked.Increment(ref _shapeAssertionSequence);
        var assertionSuffix = assertionNumber == 1 ? string.Empty : $"-{assertionNumber:00}";
        return $"{AttachmentPrefix}-expected-shape{assertionSuffix}";
    }

    /// <summary>
    /// Asserts the response status with the state this response carries and throws the exception
    /// <paramref name="failureFactory"/> builds when the assertion does not hold.
    /// </summary>
    protected void AssertStatus(
        string source,
        HttpStatusCode expected,
        bool negated,
        Func<Exception> failureFactory)
        => ProtoStatusAssertion.Assert(
            Context,
            source,
            expected,
            StatusCode,
            negated,
            failureFactory,
            parentOperationId: RequestTraceId,
            requestIdentifier: Identifier);

    /// <summary>
    /// Asserts a JSON body's shape through the shared matcher, capturing the expected shape when the
    /// protocol opted in and recording the protocol's observation from the matched properties.
    /// </summary>
    protected void AssertShape(
        string source,
        string title,
        string? actualJson,
        object expectedShape,
        JsonSerializerOptions? options,
        IReadOnlyDictionary<string, string?> extraAttributes,
        Func<IReadOnlyList<string>, ProtoObservation?> observation)
    {
        ArgumentNullException.ThrowIfNull(expectedShape);
        var attachmentName = NextExpectedShapeAttachmentName();
        ProtoShapeAssertion.Assert(
            new ProtoShapeAssertionContext(
                Context,
                source,
                title,
                ParentOperationId: RequestTraceId,
                ExtraAttributes: extraAttributes,
                CaptureExpectedShape: attachmentName is not null,
                AttachmentName: attachmentName,
                AttachmentDescription: Identifier),
            actualJson,
            expectedShape,
            options,
            AttachmentOptions,
            observation);
    }

    /// <summary>
    /// Releases the underlying HTTP response. Callers own a returned response and should dispose it when
    /// access to <see cref="RawResponse"/> is no longer required; a protocol with its own document
    /// disposes that first and then calls this.
    /// </summary>
    public virtual void Dispose() => RawResponse.Dispose();
}
