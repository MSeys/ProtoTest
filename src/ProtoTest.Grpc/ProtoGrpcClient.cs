namespace ProtoTest.Grpc;

using System.Globalization;
using global::Grpc.Core;
using global::Grpc.Net.Client;
using ProtoTest.Core;
using ProtoTest.Grpc.Authentication;

/// <summary>
/// A named gRPC client created during setup by <see cref="Clients.ProtoGrpcClientInitializer"/>. Every
/// call is traced as a <c>grpc.call</c> operation with request/response sections and sanitized metadata,
/// applies the test's <c>[Auth]</c> authenticators to the call metadata, and reports a
/// <c>grpc.response</c> observation so coverage collectors can aggregate it.
/// </summary>
public sealed class ProtoGrpcClient : IDisposable
{
    private static readonly string[] SensitiveMetadataKeys =
        ["authorization", "cookie", "set-cookie", "x-api-key", "api-key", "token", "x-auth-token"];

    private readonly ProtoExecutionContext _context;
    private readonly string _targetName;
    private readonly ProtoGrpcClientOptions _options;
    private readonly Func<ProtoExecutionContext, CancellationToken, ValueTask<GrpcChannel>> _channelFactory;
    private readonly ProtoLock _gate = new();
    private GrpcChannel? _channel;
    private CallInvoker? _invoker;

    internal ProtoGrpcClient(
        ProtoExecutionContext context,
        string targetName,
        ProtoGrpcClientOptions options,
        Func<ProtoExecutionContext, CancellationToken, ValueTask<GrpcChannel>> channelFactory)
    {
        _context = context;
        _targetName = targetName;
        _options = options;
        _channelFactory = channelFactory;
    }

    /// <summary>Creates a client over a known transport, for resolutions outside the initializer.</summary>
    internal static ProtoGrpcClient ForTransport(ProtoExecutionContext context, string name, HttpClient transport)
        => new(
            context,
            name,
            new ProtoGrpcClientOptions(),
            (_, _) => ValueTask.FromResult(GrpcChannel.ForAddress(
                transport.BaseAddress ?? new Uri("http://localhost"),
                new GrpcChannelOptions { HttpHandler = new Internal.GrpcChannelForwardingHandler(transport) })));

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
        using var operation = _context.Trace
            .Operation("grpc.call", $"gRPC · {method.FullName}", "ProtoTest.Grpc")
            .For(ProtoTraceEntityKinds.Client, $"client:{typeof(ProtoGrpcClient).FullName}:{_targetName}")
            .With("rpc.system", "grpc")
            .With("rpc.service", method.ServiceName)
            .With("rpc.method", method.Name)
            .With("client.name", _targetName)
            .With("rpc.deadline", deadline?.ToString("O", CultureInfo.InvariantCulture))
            .Begin();
        var callMetadata = await PrepareMetadataAsync(metadata, operation, cancellationToken);
        if (Format(request) is { } body)
        {
            operation.AddSection(new ProtoTraceSection("Request", ProtoTraceSectionKind.Code, Content: body, Language: "protobuf"));
        }

        try
        {
            var response = await (await GetInvokerAsync(cancellationToken))
                .AsyncUnaryCall(method, null, BuildCallOptions(callMetadata, deadline), request)
                .ConfigureAwait(false);
            operation.AddSection(ResponseSection(response));
            operation.Succeed();
            _context.RecordObservation(Observation(method.ServiceName, method.Name, "ok"));
            return response;
        }
        catch (RpcException exception)
        {
            Fail(operation, method.ServiceName, method.Name, exception);
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
        using var operation = _context.Trace
            .Operation("grpc.call", $"gRPC · {method.FullName}", "ProtoTest.Grpc")
            .For(ProtoTraceEntityKinds.Client, $"client:{typeof(ProtoGrpcClient).FullName}:{_targetName}")
            .With("rpc.system", "grpc")
            .With("rpc.service", method.ServiceName)
            .With("rpc.method", method.Name)
            .With("client.name", _targetName)
            .With("rpc.deadline", deadline?.ToString("O", CultureInfo.InvariantCulture))
            .Begin();
        var callMetadata = await PrepareMetadataAsync(metadata, operation, cancellationToken);

        try
        {
            var call = (await GetInvokerAsync(cancellationToken))
                .AsyncClientStreamingCall(method, null, BuildCallOptions(callMetadata, deadline));
            foreach (var request in requests)
            {
                await call.RequestStream.WriteAsync(request).ConfigureAwait(false);
            }

            await call.RequestStream.CompleteAsync().ConfigureAwait(false);
            var response = await call.ResponseAsync.ConfigureAwait(false);
            operation.AddSection(ResponseSection(response));
            operation.Succeed();
            _context.RecordObservation(Observation(method.ServiceName, method.Name, "ok"));
            return response;
        }
        catch (RpcException exception)
        {
            Fail(operation, method.ServiceName, method.Name, exception);
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
        using var operation = _context.Trace
            .Operation("grpc.call", $"gRPC · {method.FullName}", "ProtoTest.Grpc")
            .For(ProtoTraceEntityKinds.Client, $"client:{typeof(ProtoGrpcClient).FullName}:{_targetName}")
            .With("rpc.system", "grpc")
            .With("rpc.service", method.ServiceName)
            .With("rpc.method", method.Name)
            .With("client.name", _targetName)
            .With("rpc.deadline", deadline?.ToString("O", CultureInfo.InvariantCulture))
            .Begin();
        var callMetadata = await PrepareMetadataAsync(metadata, operation, cancellationToken);
        if (Format(request) is { } body)
        {
            operation.AddSection(new ProtoTraceSection("Request", ProtoTraceSectionKind.Code, Content: body, Language: "protobuf"));
        }

        var responses = new List<TResponse>();
        try
        {
            var call = (await GetInvokerAsync(cancellationToken))
                .AsyncServerStreamingCall(method, null, BuildCallOptions(callMetadata, deadline), request);
            await foreach (var response in call.ResponseStream.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                responses.Add(response);
            }

            operation.AddSection(ResponseSection(responses));
            operation.Succeed();
            _context.RecordObservation(Observation(method.ServiceName, method.Name, "ok"));
            return responses;
        }
        catch (RpcException exception)
        {
            Fail(operation, method.ServiceName, method.Name, exception);
            throw;
        }
    }

    /// <summary>Opens a server-streaming call for callers that want to consume messages as they arrive.</summary>
    public AsyncServerStreamingCall<TResponse> ServerStreaming<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        TRequest request,
        Action<Metadata>? metadata = null,
        DateTime? deadline = null)
        where TRequest : class
        where TResponse : class
    {
        ArgumentNullException.ThrowIfNull(method);
        var callMetadata = BuildRawMetadata(metadata);
        return GetInvokerAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult()
            .AsyncServerStreamingCall(method, null, BuildCallOptions(callMetadata, deadline), request);
    }

    /// <summary>Opens a duplex-streaming call for callers that drive both directions themselves.</summary>
    public AsyncDuplexStreamingCall<TRequest, TResponse> DuplexStreaming<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        Action<Metadata>? metadata = null,
        DateTime? deadline = null)
        where TRequest : class
        where TResponse : class
    {
        ArgumentNullException.ThrowIfNull(method);
        var callMetadata = BuildRawMetadata(metadata);
        return GetInvokerAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult()
            .AsyncDuplexStreamingCall(method, null, BuildCallOptions(callMetadata, deadline));
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _channel?.Dispose();
            _channel = null;
            _invoker = null;
        }
    }

    private async ValueTask<CallInvoker> GetInvokerAsync(CancellationToken cancellationToken)
    {
        if (_invoker is not null)
        {
            return _invoker;
        }

        var channel = await _channelFactory(_context, cancellationToken);
        lock (_gate)
        {
            _channel ??= channel;
            _invoker ??= _channel.CreateCallInvoker();
            return _invoker;
        }
    }

    private async ValueTask<Metadata> PrepareMetadataAsync(
        Action<Metadata>? metadata,
        ProtoTraceOperation operation,
        CancellationToken cancellationToken)
    {
        // User metadata first, then authenticators: an authenticator may intentionally override.
        var callMetadata = BuildRawMetadata(metadata);
        var state = _context.TryResolve<GrpcContextState>();
        await ProtoGrpcAuthenticationApplier.ApplyAsync(
            state?.AuthenticatorFactory,
            callMetadata,
            _context,
            _targetName,
            operation,
            cancellationToken);
        foreach (var pair in MetadataAttributes(callMetadata))
        {
            operation.SetAttribute(pair.Key, pair.Value);
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

    private CallOptions BuildCallOptions(Metadata metadata, DateTime? deadline)
        => new(
            headers: metadata,
            deadline: deadline ?? (_options.DefaultDeadline is { } defaultDeadline
                ? DateTime.UtcNow + defaultDeadline
                : null));

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
        operation.Fail(exception);
        _context.RecordObservation(Observation(service, name, exception.StatusCode.ToString()));
    }

    private ProtoObservation Observation(string service, string name, string status)
        => new(
            _targetName,
            "grpc.response",
            $"{service}/{name}",
            Data: null,
            Metadata: new Dictionary<string, object>
            {
                ["rpc.system"] = "grpc",
                ["rpc.service"] = service,
                ["rpc.method"] = name,
                ["rpc.grpc.status"] = status
            });

    private static Dictionary<string, string?> MetadataAttributes(Metadata metadata)
    {
        var attributes = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var entry in metadata)
        {
            attributes[$"rpc.metadata.{entry.Key}"] = IsSensitive(entry.Key) ? "(redacted)" : entry.Value;
        }

        return attributes;
    }

    private static bool IsSensitive(string key)
        => SensitiveMetadataKeys.Any(sensitive =>
            key.Contains(sensitive, StringComparison.OrdinalIgnoreCase));

    private static ProtoTraceSection ResponseSection(object? response)
        => new("Response", ProtoTraceSectionKind.Code, Content: Format(response), Language: "protobuf");

    private static string? Format(object? value) => value switch
    {
        null => null,
        string text => text,
        IEnumerable<object> items => string.Join(
            Environment.NewLine,
            items.Select(item => Format(item) ?? string.Empty)),
        _ => value.ToString()
    };
}
