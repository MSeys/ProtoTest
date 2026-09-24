namespace ProtoTest.Grpc;

using System.Globalization;
using global::Google.Protobuf;
using global::Grpc.Core;
using global::Grpc.Net.Client;
using ProtoTest.Core;
using ProtoTest.Grpc.Authentication;
using ProtoTest.Http;
using ProtoTest.Json;

/// <summary>
/// A named gRPC client created during setup by <see cref="Clients.ProtoGrpcClientInitializer"/>. Every
/// call is traced as a <c>grpc.call</c> operation with request/response sections and sanitized metadata,
/// applies the test's <c>[Auth]</c> authenticators to the call metadata, and reports a
/// <c>grpc.response</c> observation so coverage collectors can aggregate it.
/// </summary>
public sealed partial class ProtoGrpcClient : IDisposable
{
    private const int MaxCapturedStreamMessages = 10;

    private static readonly ProtoAttachmentFailure AttachmentFailure =
        new("grpc.attachment.failed", ProtoGrpcBuilder.Protocol.TraceSource, "gRPC attachment");


    private readonly ProtoExecutionContext _context;
    private readonly string _targetName;
    private readonly string _entityName;
    private readonly GrpcClientOptions _options;
    private readonly Func<ProtoExecutionContext, CancellationToken, ValueTask<GrpcChannel>> _channelFactory;
    private readonly ProtoLock _gate = new();
    private int _callSequence;
    private bool _disposed;
    private GrpcChannel? _channel;
    private CallInvoker? _invoker;

    internal ProtoGrpcClient(
        ProtoExecutionContext context,
        string targetName,
        GrpcClientOptions options,
        Func<ProtoExecutionContext, CancellationToken, ValueTask<GrpcChannel>> channelFactory,
        string? entityName = null)
    {
        _context = context;
        _targetName = targetName;
        _entityName = entityName ?? targetName;
        _options = options;
        _channelFactory = channelFactory;
        Blocking = new ProtoGrpcBlockingClient(this);
    }

    /// <summary>Creates a client over a known transport, for resolutions outside the initializer.</summary>
    internal static ProtoGrpcClient ForTransport(
        ProtoExecutionContext context,
        string name,
        HttpClient transport,
        GrpcClientOptions options,
        string entityName)
        => new(
            context,
            name,
            options,
            (_, _) => ValueTask.FromResult(GrpcChannel.ForAddress(
                transport.BaseAddress ?? new Uri("http://localhost"),
                new GrpcChannelOptions { HttpHandler = new Internal.GrpcChannelForwardingHandler(transport) })),
            entityName);

    /// <summary>Calls a unary method and waits for its response.</summary>
    public async Task<TResponse> UnaryAsync<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        TRequest request,
        Action<Metadata>? metadata = null,
        DateTime? deadline = null,
        CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class
    {
        ArgumentNullException.ThrowIfNull(method);
        var callNumber = Interlocked.Increment(ref _callSequence);
        using var operation = BeginCallTrace(method, deadline);
        var callMetadata = await PrepareMetadataAsync(metadata, operation, cancellationToken);
        var attachmentOptions = _context.TryService<GrpcAttachmentOptions>();
        if (attachmentOptions?.CaptureRequestBodies == true)
        {
            CaptureSingle(
                operation,
                attachmentOptions,
                AttachmentName(_targetName, method, "request", callNumber),
                request,
                $"{_targetName} · {method.FullName} request");
        }

        if (Format(request) is { } body)
        {
            operation.AddSection(new ProtoTraceSection("Request", ProtoTraceSectionKind.Code, Content: body, Language: "protobuf"));
        }

        try
        {
            var response = await (await GetInvokerAsync(cancellationToken))
                .AsyncUnaryCall(method, null, BuildCallOptions(callMetadata, deadline, cancellationToken), request)
                .ConfigureAwait(false);
            CompleteCall(
                operation,
                method,
                callNumber,
                attachmentOptions,
                responseCount: 1,
                stream: false,
                [response],
                op => op.AddSection(ResponseSection(response)));
            return response;
        }
        catch (RpcException exception)
        {
            Fail(operation, method.ServiceName, method.Name, exception);
            throw;
        }
        catch (Exception exception)
        {
            operation.Fail(exception);
            throw;
        }
    }

    /// <summary>Sends every request on a client-streaming method and waits for its response.</summary>
    public async Task<TResponse> ClientStreamingAsync<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        IEnumerable<TRequest> requests,
        Action<Metadata>? metadata = null,
        DateTime? deadline = null,
        CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(requests);
        var callNumber = Interlocked.Increment(ref _callSequence);
        using var operation = BeginCallTrace(method, deadline);
        var callMetadata = await PrepareMetadataAsync(metadata, operation, cancellationToken);
        var requestList = requests as IReadOnlyCollection<TRequest> ?? [.. requests];
        operation.SetAttribute("grpc.request.count", requestList.Count.ToString(CultureInfo.InvariantCulture));
        var attachmentOptions = _context.TryService<GrpcAttachmentOptions>();
        if (attachmentOptions?.CaptureRequestBodies == true)
        {
            CaptureStream(
                operation,
                attachmentOptions,
                AttachmentName(_targetName, method, "request", callNumber),
                requestList.Cast<object>(),
                $"{_targetName} · {method.FullName} request · {requestList.Count} messages");
        }

        if (Format(requestList) is { } body)
        {
            operation.AddSection(new ProtoTraceSection("Request", ProtoTraceSectionKind.Code, Content: body, Language: "protobuf"));
        }

        try
        {
            using var call = (await GetInvokerAsync(cancellationToken))
                .AsyncClientStreamingCall(method, null, BuildCallOptions(callMetadata, deadline, cancellationToken));
            foreach (var request in requestList)
            {
                await call.RequestStream.WriteAsync(request).ConfigureAwait(false);
            }

            await call.RequestStream.CompleteAsync().ConfigureAwait(false);
            var response = await call.ResponseAsync.ConfigureAwait(false);
            CompleteCall(
                operation,
                method,
                callNumber,
                attachmentOptions,
                responseCount: 1,
                stream: false,
                [response],
                op => op.AddSection(ResponseSection(response)));
            return response;
        }
        catch (RpcException exception)
        {
            Fail(operation, method.ServiceName, method.Name, exception);
            throw;
        }
        catch (Exception exception)
        {
            operation.Fail(exception);
            throw;
        }
    }

    /// <summary>Reads a server-streaming method to completion and returns the response messages.</summary>
    public async Task<IReadOnlyList<TResponse>> ServerStreamingAsync<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        TRequest request,
        Action<Metadata>? metadata = null,
        DateTime? deadline = null,
        CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class
    {
        ArgumentNullException.ThrowIfNull(method);
        var callNumber = Interlocked.Increment(ref _callSequence);
        using var operation = BeginCallTrace(method, deadline);
        var callMetadata = await PrepareMetadataAsync(metadata, operation, cancellationToken);
        var attachmentOptions = _context.TryService<GrpcAttachmentOptions>();
        if (attachmentOptions?.CaptureRequestBodies == true)
        {
            CaptureSingle(
                operation,
                attachmentOptions,
                AttachmentName(_targetName, method, "request", callNumber),
                request,
                $"{_targetName} · {method.FullName} request");
        }

        if (Format(request) is { } body)
        {
            operation.AddSection(new ProtoTraceSection("Request", ProtoTraceSectionKind.Code, Content: body, Language: "protobuf"));
        }

        var responses = new List<TResponse>();
        try
        {
            using var call = (await GetInvokerAsync(cancellationToken))
                .AsyncServerStreamingCall(method, null, BuildCallOptions(callMetadata, deadline, cancellationToken), request);
            await foreach (var response in call.ResponseStream.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                responses.Add(response);
            }

            CompleteCall(
                operation,
                method,
                callNumber,
                attachmentOptions,
                responses.Count,
                stream: true,
                responses,
                op => op.AddSection(ResponseSection(responses)));
            return responses;
        }
        catch (RpcException exception)
        {
            Fail(operation, method.ServiceName, method.Name, exception);
            throw;
        }
        catch (Exception exception)
        {
            operation.Fail(exception);
            throw;
        }
    }

    /// <summary>Opens a raw server-streaming call without blocking the caller.</summary>
    public async Task<AsyncServerStreamingCall<TResponse>> OpenServerStreamingAsync<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        TRequest request,
        Action<Metadata>? metadata = null,
        DateTime? deadline = null,
        CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class
    {
        ArgumentNullException.ThrowIfNull(method);
        // Raw calls stay untraced, so there is no operation to attach auth attributes to; metadata
        // still goes through the shared authenticator pipeline before the channel is awaited.
        var callMetadata = await PrepareMetadataAsync(metadata, operation: null, cancellationToken);
        var invoker = await GetInvokerAsync(cancellationToken);
        return invoker.AsyncServerStreamingCall(method, null, BuildCallOptions(callMetadata, deadline, cancellationToken), request);
    }

    /// <summary>Opens a raw duplex-streaming call without blocking the caller.</summary>
    public async Task<AsyncDuplexStreamingCall<TRequest, TResponse>> OpenDuplexStreamingAsync<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        Action<Metadata>? metadata = null,
        DateTime? deadline = null,
        CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class
    {
        ArgumentNullException.ThrowIfNull(method);
        var callMetadata = await PrepareMetadataAsync(metadata, operation: null, cancellationToken);
        var invoker = await GetInvokerAsync(cancellationToken);
        return invoker.AsyncDuplexStreamingCall(method, null, BuildCallOptions(callMetadata, deadline, cancellationToken));
    }

    /// <summary>
    /// The synchronous entry points for callers whose test code cannot await. Each blocks the calling
    /// thread while authenticators and the channel are prepared; prefer the async opens where possible.
    /// </summary>
    public ProtoGrpcBlockingClient Blocking { get; }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _channel?.Dispose();
            _channel = null;
            _invoker = null;
        }
    }

    internal async ValueTask<CallInvoker> GetInvokerAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_invoker is not null)
            {
                return _invoker;
            }
        }

        var channel = await _channelFactory(_context, cancellationToken);
        lock (_gate)
        {
            // A call racing Dispose must not install a channel after release, and a channel that lost
            // the creation race is disposed here instead of leaking.
            if (_disposed)
            {
                channel.Dispose();
                throw new ObjectDisposedException(nameof(ProtoGrpcClient));
            }

            if (_channel is null)
            {
                _channel = channel;
                _invoker = channel.CreateCallInvoker();
            }
            else
            {
                channel.Dispose();
            }

            return _invoker!;
        }
    }

    private async ValueTask<Metadata> PrepareMetadataAsync(
        Action<Metadata>? metadata,
        ProtoTraceOperation? operation,
        CancellationToken cancellationToken)
    {
        // User metadata first, then authenticators: an authenticator may intentionally override.
        var callMetadata = BuildRawMetadata(metadata);
        var state = _context.TryResolve<ProtoHttpContextState>(ProtoGrpcBuilder.Protocol.Key);
        await ProtoGrpcAuthenticationApplier.ApplyAsync(
            state?.AuthenticatorFactory,
            callMetadata,
            _context,
            _targetName,
            operation,
            cancellationToken);
        if (operation is not null)
        {
            foreach (var pair in MetadataAttributes(callMetadata))
            {
                operation.SetAttribute(pair.Key, pair.Value);
            }
        }

        return callMetadata;
    }

    private Metadata BuildRawMetadata(Action<Metadata>? metadata)
    {
        var result = new Metadata();
        foreach (var pair in _options.Metadata)
        {
            result.Add(pair.Key, pair.Value);
        }

        _options.ConfigureMetadata?.Invoke(_context, result);
        metadata?.Invoke(result);
        return result;
    }

    private CallOptions BuildCallOptions(Metadata metadata, DateTime? deadline, CancellationToken cancellationToken)
        => new(
            headers: metadata,
            deadline: deadline ?? (_options.DefaultDeadline is { } defaultDeadline
                ? DateTime.UtcNow + defaultDeadline
                : null),
            cancellationToken: cancellationToken);

    private void Fail(ProtoTraceOperation operation, string service, string name, RpcException exception)
    {
        operation.SetAttribute("rpc.grpc.status_code", ((int)exception.StatusCode).ToString(CultureInfo.InvariantCulture));
        operation.SetAttribute("rpc.grpc.status", exception.StatusCode.ToString());
        operation.AddSection(new ProtoTraceSection(
            "Status",
            ProtoTraceSectionKind.Fields,
            Items:
            [
                new ProtoTraceSectionItem("code", exception.StatusCode.ToString()),
                new ProtoTraceSectionItem("detail", exception.Status.Detail)
            ]));
        // A call the caller cancelled is not a product failure, even when gRPC reports it as a status
        // code rather than an OperationCanceledException.
        if (exception.StatusCode == StatusCode.Cancelled)
        {
            operation.Cancel(exception);
        }
        else
        {
            operation.Fail(exception);
        }

        _context.RecordObservation(Observation(
            service,
            name,
            exception.StatusCode.ToString(),
            ProtoGrpcBuilder.FailureObservationKind));
    }

    private ProtoObservation Observation(string service, string name, string status, string kind)
        => new(
            _targetName,
            kind,
            $"{service}/{name}",
            Data: null,
            Metadata: new Dictionary<string, object>
            {
                ["rpc.system"] = "grpc",
                ["rpc.service"] = service,
                ["rpc.method"] = name,
                ["rpc.grpc.status"] = status
            });

    private Dictionary<string, string?> MetadataAttributes(Metadata metadata)
    {
        var attributes = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var entry in metadata)
        {
            attributes[$"rpc.metadata.{entry.Key}"] = IsSensitive(entry.Key) ? "(redacted)" : entry.Value;
        }

        return attributes;
    }

    private bool IsSensitive(string key)
        => _options.SensitiveMetadataKeys.Any(sensitive =>
            key.Contains(sensitive, StringComparison.OrdinalIgnoreCase));

    /// <summary>Opens the traced call operation every call shape shares.</summary>
    private ProtoTraceOperation BeginCallTrace(IMethod method, DateTime? deadline)
        => _context.Trace
            .Operation("grpc.call", $"gRPC · {method.FullName}", ProtoGrpcBuilder.Protocol.TraceSource)
            .ForClient(typeof(ProtoGrpcClient), _targetName, _entityName)
            .With("rpc.system", "grpc")
            .With("rpc.service", method.ServiceName)
            .With("rpc.method", method.Name)
            .With("client.name", _targetName)
            .With("rpc.deadline", deadline?.ToString("O", CultureInfo.InvariantCulture))
            .Begin();

    /// <summary>
    /// Captures the response when the test asked for it, records the response section and count, and
    /// succeeds the call with its observation. The call shapes supply only their payload.
    /// </summary>
    private void CompleteCall(
        ProtoTraceOperation operation,
        IMethod method,
        int callNumber,
        GrpcAttachmentOptions? attachmentOptions,
        int responseCount,
        bool stream,
        IReadOnlyList<object> responseMessages,
        Action<ProtoTraceOperation> addResponseSection)
    {
        operation.SetAttribute("grpc.response.count", responseCount.ToString(CultureInfo.InvariantCulture));
        if (attachmentOptions?.CaptureResponses == true)
        {
            var attachmentName = AttachmentName(_targetName, method, "response", callNumber);
            var description = $"{_targetName} · {method.FullName} response";
            if (stream)
            {
                CaptureStream(
                    operation,
                    attachmentOptions,
                    attachmentName,
                    responseMessages,
                    $"{description} · {responseCount} messages");
            }
            else
            {
                CaptureSingle(operation, attachmentOptions, attachmentName, responseMessages[0], description);
            }
        }

        addResponseSection(operation);
        operation.Succeed();
        _context.RecordObservation(Observation(method.ServiceName, method.Name, "ok", ProtoGrpcBuilder.Protocol.ResponseObservationKind));
    }

}
