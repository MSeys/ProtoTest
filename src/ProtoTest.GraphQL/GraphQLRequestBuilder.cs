namespace ProtoTest.GraphQL;

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.GraphQL.Internal;
using ProtoTest.Json;

public sealed class GraphQLRequestBuilder
{
    private static readonly MediaTypeWithQualityHeaderValue GraphQLMediaType = new("application/graphql-response+json");
    private readonly HttpClient _client;
    private readonly ProtoExecutionContext _context;
    private readonly string _targetName;
    private readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);
    private GraphQLBuiltOperation? _operation;
    private Func<ProtoExecutionContext, IProtoHttpAuthenticator>? _authenticatorFactory;
    private IProtoHttpAuthenticator? _authenticator;
    private object? _variables;
    private Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>>? _baseAddressResolver;
    private string? _simpleOperationType;
    private string? _simpleRootField;
    private object? _simpleArguments;
    private string? _simpleOperationName;
    private GraphQLSubscriptionTransport _subscriptionTransport = GraphQLSubscriptionTransport.WebSocket;
    private object? _connectionPayload;

    internal GraphQLRequestBuilder(HttpClient client, ProtoExecutionContext context, string targetName)
    {
        _client = client;
        _context = context;
        _targetName = targetName;
    }

    public GraphQLRequestBuilder Query(string? name, Action<GraphQLOperationBuilder> configure)
        => Operation("query", name, configure);

    public GraphQLRequestBuilder Mutation(string? name, Action<GraphQLOperationBuilder> configure)
        => Operation("mutation", name, configure);

    public GraphQLRequestBuilder Subscription(string? name, Action<GraphQLOperationBuilder> configure)
        => Operation("subscription", name, configure);

    /// <summary>Starts a shape-driven query for one root field.</summary>
    public GraphQLRequestBuilder Query(string rootField, object? arguments = null, string? operationName = null)
        => SimpleOperation("query", rootField, arguments, operationName);

    /// <summary>Starts a shape-driven mutation for one root field.</summary>
    public GraphQLRequestBuilder Mutation(string rootField, object? arguments = null, string? operationName = null)
        => SimpleOperation("mutation", rootField, arguments, operationName);

    /// <summary>Starts a shape-driven subscription for one root field.</summary>
    public GraphQLRequestBuilder Subscription(string rootField, object? arguments = null, string? operationName = null)
        => SimpleOperation("subscription", rootField, arguments, operationName);

    /// <summary>Derives the GraphQL selection set from an anonymous object or test-owned contract.</summary>
    public GraphQLRequestBuilder Select<TShape>(TShape selectionShape)
    {
        ArgumentNullException.ThrowIfNull(selectionShape);
        return BuildSimpleSelection(selectionShape, selectionShape.GetType());
    }

    /// <summary>Derives the GraphQL selection set from a test-owned contract type.</summary>
    public GraphQLRequestBuilder Select<TShape>()
        => BuildSimpleSelection(null, typeof(TShape));

    /// <summary>Selects, executes, and matches one root field using the same response shape.</summary>
    public async Task<GraphQLResponse> ExpectAsync<TShape>(
        TShape expectedShape,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedShape);
        _ = _simpleRootField
            ?? throw new InvalidOperationException("ExpectAsync is available after a shape-driven Query or Mutation.");
        Select(expectedShape);
        var response = await ExecuteAsync(cancellationToken);
        try
        {
            response.ShouldMatchData(expectedShape);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    public GraphQLRequestBuilder Request(string document, string? operationName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(document);
        var parsed = HotChocolate.Language.Utf8GraphQLParser.Parse(document);
        var operation = parsed.Definitions.OfType<HotChocolate.Language.OperationDefinitionNode>()
            .FirstOrDefault(definition => operationName is null || definition.Name?.Value == operationName)
            ?? throw new ArgumentException("The GraphQL document does not contain the requested operation.", nameof(document));
        ClearSimpleOperation();
        _operation = new GraphQLBuiltOperation(
            document,
            parsed,
            operation.Operation.ToString().ToLowerInvariant(),
            operation.Name?.Value);
        TraceConfiguration(
            "graphql.operation.configure",
            $"Configure · {_operation.Type} {_operation.Name ?? "<anonymous>"}",
            new Dictionary<string, string?>
            {
                ["graphql.operation.type"] = _operation.Type,
                ["graphql.operation.name"] = _operation.Name,
                ["graphql.document.source"] = "raw"
            });
        return this;
    }

    public GraphQLRequestBuilder Header(string name, string value)
    {
        _headers[name] = value;
        TraceConfiguration("http.header.configure", $"Header · {name}", new Dictionary<string, string?>()
        {
            ["http.header.name"] = name,
            ["http.header.value_recorded"] = "false"
        });
        return this;
    }

    public GraphQLRequestBuilder Variables(object variables)
    {
        _variables = variables ?? throw new ArgumentNullException(nameof(variables));
        TraceConfiguration("graphql.variables.configure", "GraphQL variables configured", new Dictionary<string, string?>()
        {
            ["variables.type"] = variables.GetType().FullName
        });
        return this;
    }

    /// <summary>Sets the optional graphql-transport-ws connection_init payload.</summary>
    public GraphQLRequestBuilder ConnectionPayload(object payload)
    {
        _connectionPayload = payload ?? throw new ArgumentNullException(nameof(payload));
        TraceConfiguration(
            "graphql.subscription.connection_payload.configure",
            "GraphQL subscription connection payload configured",
            new Dictionary<string, string?> { ["payload.type"] = payload.GetType().FullName });
        return this;
    }

    public GraphQLRequestBuilder Auth(IProtoHttpAuthenticator authenticator)
    {
        _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        _authenticatorFactory = _ => authenticator;
        TraceConfiguration("auth.select", $"Authentication · {authenticator.GetType().Name}", new Dictionary<string, string?>()
        {
            ["auth.source"] = "request",
            ["auth.type"] = authenticator.GetType().FullName
        });
        return this;
    }

    public GraphQLRequestBuilder Auth<TAuthenticator>(params object[] constructorArgs)
        where TAuthenticator : class, IProtoHttpAuthenticator
    {
        _authenticator = null;
        _authenticatorFactory = context => ProtoAuthenticatorFactory.Create<TAuthenticator>(context, constructorArgs);
        TraceConfiguration("auth.select", $"Authentication · {typeof(TAuthenticator).Name}", new Dictionary<string, string?>()
        {
            ["auth.source"] = "request",
            ["auth.type"] = typeof(TAuthenticator).FullName
        });
        return this;
    }

    public GraphQLRequestBuilder WithoutAuth()
    {
        _authenticator = null;
        _authenticatorFactory = null;
        TraceConfiguration("auth.disable", "Authentication · Disabled", new Dictionary<string, string?> { ["auth.source"] = "request" });
        return this;
    }

    internal GraphQLRequestBuilder UseAuthenticatorFactory(Func<ProtoExecutionContext, IProtoHttpAuthenticator>? factory)
    {
        _authenticatorFactory = factory;
        return this;
    }

    internal GraphQLRequestBuilder UseBaseAddressResolver(
        Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>>? resolver)
    {
        _baseAddressResolver = resolver;
        return this;
    }

    internal GraphQLRequestBuilder UseSubscriptionTransport(GraphQLSubscriptionTransport transport)
    {
        _subscriptionTransport = transport;
        return this;
    }

    public async Task<GraphQLResponse> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var operation = _operation ?? throw new InvalidOperationException("Configure a query, mutation, or request before executing it.");
        if (operation.Type == "subscription")
            throw new InvalidOperationException("Subscriptions return a stream. Use SubscribeAsync instead of ExecuteAsync.");
        var identifier = $"{operation.Type} {operation.Name ?? "<anonymous>"}";
        using var traceOperation = _context.Trace
            .Operation("graphql.operation", $"GraphQL · {identifier}", "ProtoTest.GraphQL")
            .With("client.name", _targetName)
            .With("graphql.operation.type", operation.Type)
            .With("graphql.operation.name", operation.Name)
            .Begin();
        var stopwatch = Stopwatch.StartNew();
        Uri endpoint;
        using var resolveOperation = _context.Trace
            .Operation("graphql.endpoint.resolve", $"Resolve endpoint · {_targetName}", "ProtoTest.GraphQL")
            .With("client.name", _targetName)
            .Begin();
        try
        {
            endpoint = _baseAddressResolver is not null
                ? await _baseAddressResolver(_context, cancellationToken)
                : _client.BaseAddress ?? throw new InvalidOperationException($"GraphQL client '{_targetName}' has no endpoint.");
            if (!endpoint.IsAbsoluteUri || endpoint.Scheme is not ("http" or "https"))
                throw new InvalidOperationException("A per-test GraphQL endpoint must be an absolute HTTP or HTTPS URI.");
            resolveOperation.SetAttribute("server.address", endpoint.GetLeftPart(UriPartial.Authority));
            resolveOperation.Succeed();
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            resolveOperation.Fail(exception);
            traceOperation.Fail(exception);
            TryRecordFailure(operation, identifier, stopwatch.Elapsed, exception);
            throw;
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Accept.Add(GraphQLMediaType);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json", 0.9));
        foreach (var header in _headers) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        var requestContent = GraphQLRequestContent.Create(operation.DocumentText, operation.Name, _variables);
        var requestEnvelope = requestContent.DiagnosticJson;
        var variablesJson = requestContent.VariablesJson;
        request.Content = requestContent.Content;
        if (requestContent.RequiresPreflight)
            request.Headers.TryAddWithoutValidation("GraphQL-preflight", "1");

        var attachmentOptions = _context.TryService<GraphQLAttachmentOptions>();
        var requestNumber = attachmentOptions is null
            ? (int?)null
            : (_context.TryContext<GraphQLContextState>() ?? throw new InvalidOperationException("GraphQL context state was not initialized.")).NextRequestNumber();
        var attachmentPrefix = requestNumber is null ? null : $"graphql-{requestNumber:00}";
        if (attachmentOptions?.CaptureRequestBodies == true)
            _context.AddAttachment(
                $"{attachmentPrefix}-request",
                JsonDiagnosticSanitizer.Sanitize(requestEnvelope, attachmentOptions),
                "application/json",
                identifier);

        try
        {
            _authenticator = await ProtoHttpAuthenticationApplier.ApplyAsync(
                _authenticatorFactory,
                _authenticator,
                request,
                _context,
                _targetName,
                "GraphQL",
                "ProtoTest.GraphQL",
                cancellationToken);

            HttpResponseMessage? rawResponse = null;
            try
            {
                rawResponse = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                await ProtoHttpResponseBuffer.BufferAsync(
                    rawResponse,
                    _context.TryService<GraphQLResponseOptions>()?.MaxResponseBodyBytes ?? 10 * 1024 * 1024,
                    cancellationToken);
                var content = await rawResponse.Content.ReadAsStringAsync(cancellationToken);
                stopwatch.Stop();
                var response = new GraphQLResponse(rawResponse, content, stopwatch.Elapsed, _context, _targetName, identifier, operation,
                    attachmentOptions, attachmentPrefix, traceOperation.Id, _simpleRootField);

                if (attachmentOptions?.CaptureResponses == true)
                    _context.AddAttachment(
                        $"{attachmentPrefix}-response",
                        JsonDiagnosticSanitizer.Sanitize(content, attachmentOptions),
                        rawResponse.Content.Headers.ContentType?.MediaType ?? "application/json",
                        identifier);

                _context.RecordObservation(new ProtoObservation(
                    _targetName,
                    "graphql.response",
                    identifier,
                    new GraphQLResponseData(
                        operation.Type,
                        operation.Name,
                        operation.DocumentText,
                        (int)rawResponse.StatusCode,
                        response.Errors.Count,
                        response.Errors.Select(error => error.Code).Where(code => code is not null).Cast<string>().ToArray(),
                        stopwatch.Elapsed,
                        variablesJson is null ? null : JsonDiagnosticSanitizer.Sanitize(variablesJson, attachmentOptions, truncate: false))));
                traceOperation
                    .SetAttribute("http.response.status_code", ((int)rawResponse.StatusCode).ToString())
                    .SetAttribute("graphql.error.count", response.Errors.Count.ToString());
                traceOperation.Succeed();
                rawResponse = null; // GraphQLResponse owns the response from here on.
                return response;
            }
            finally
            {
                rawResponse?.Dispose();
            }
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            traceOperation.Fail(exception);
            TryRecordFailure(operation, identifier, stopwatch.Elapsed, exception);
            throw;
        }
    }

    public async Task<GraphQLSubscription> SubscribeAsync(CancellationToken cancellationToken = default)
    {
        var operation = _operation
            ?? throw new InvalidOperationException("Configure a subscription before subscribing.");
        if (operation.Type != "subscription")
            throw new InvalidOperationException("SubscribeAsync requires a subscription operation.");

        var identifier = $"subscription {operation.Name ?? "<anonymous>"}";
        var stopwatch = Stopwatch.StartNew();
        var endpoint = _baseAddressResolver is not null
            ? await _baseAddressResolver(_context, cancellationToken)
            : _client.BaseAddress ?? throw new InvalidOperationException($"GraphQL client '{_targetName}' has no endpoint.");
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("A per-test GraphQL endpoint must be an absolute HTTP or HTTPS URI.");

        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        HttpResponseMessage? rawResponse = null;
        WebSocket? rawSocket = null;
        try
        {
            if (_subscriptionTransport == GraphQLSubscriptionTransport.Sse)
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            foreach (var header in _headers) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            var requestContent = GraphQLRequestContent.Create(operation.DocumentText, operation.Name, _variables);
            request.Content = requestContent.Content;
            if (requestContent.RequiresPreflight)
                request.Headers.TryAddWithoutValidation("GraphQL-preflight", "1");

            _authenticator = await ProtoHttpAuthenticationApplier.ApplyAsync(
                _authenticatorFactory,
                _authenticator,
                request,
                _context,
                _targetName,
                "GraphQL",
                "ProtoTest.GraphQL",
                cancellationToken);

            var attachmentOptions = _context.TryService<GraphQLAttachmentOptions>();
            var requestNumber = attachmentOptions is null
                ? (int?)null
                : (_context.TryContext<GraphQLContextState>()
                    ?? throw new InvalidOperationException("GraphQL context state was not initialized.")).NextRequestNumber();
            var attachmentPrefix = requestNumber is null ? null : $"graphql-{requestNumber:00}";
            if (attachmentOptions?.CaptureRequestBodies == true)
                _context.AddAttachment(
                    $"{attachmentPrefix}-request",
                    JsonDiagnosticSanitizer.Sanitize(requestContent.DiagnosticJson, attachmentOptions),
                    "application/json",
                    identifier);

            var variablesJson = requestContent.VariablesJson is null
                ? null
                : JsonDiagnosticSanitizer.Sanitize(
                    requestContent.VariablesJson,
                    attachmentOptions,
                    truncate: false);

            if (_subscriptionTransport == GraphQLSubscriptionTransport.WebSocket)
            {
                var webSocketEndpoint = ToWebSocketUri(endpoint);
                var headers = request.Headers.ToDictionary(
                    header => header.Key,
                    header => string.Join(", ", header.Value),
                    StringComparer.OrdinalIgnoreCase);
                rawSocket = await _context.Services.GetRequiredService<IGraphQLWebSocketFactory>()
                    .ConnectAsync(webSocketEndpoint, headers, cancellationToken);
                await GraphQLWebSocketProtocol.SendAsync(
                    rawSocket,
                    new { type = "connection_init", payload = _connectionPayload },
                    cancellationToken);
                await AwaitConnectionAcknowledgementAsync(rawSocket, cancellationToken);
                using var envelope = JsonDocument.Parse(requestContent.DiagnosticJson);
                await GraphQLWebSocketProtocol.SendAsync(
                    rawSocket,
                    new
                    {
                        id = "1",
                        type = "subscribe",
                        payload = envelope.RootElement.Clone()
                    },
                    cancellationToken);
                request.Dispose();
                TraceSubscriptionStarted(operation, "websocket", null);
                var webSocketSubscription = new GraphQLSubscription(
                    rawSocket,
                    _context.TryService<GraphQLResponseOptions>()?.MaxResponseBodyBytes ?? 10 * 1024 * 1024,
                    stopwatch,
                    _context,
                    _targetName,
                    identifier,
                    operation,
                    attachmentOptions,
                    attachmentPrefix,
                    variablesJson,
                    _simpleRootField);
                rawSocket = null;
                return webSocketSubscription;
            }

            rawResponse = await _client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            var stream = await rawResponse.Content.ReadAsStreamAsync(cancellationToken);
            request.Dispose();
            TraceSubscriptionStarted(operation, "sse", (int)rawResponse.StatusCode);
            var subscription = new GraphQLSubscription(
                rawResponse,
                stream,
                stopwatch,
                _context,
                _targetName,
                identifier,
                operation,
                attachmentOptions,
                attachmentPrefix,
                variablesJson,
                _simpleRootField);
            rawResponse = null;
            return subscription;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            request.Dispose();
            rawResponse?.Dispose();
            rawSocket?.Dispose();
            TryRecordFailure(operation, identifier, stopwatch.Elapsed, exception);
            throw;
        }
    }

    private async Task AwaitConnectionAcknowledgementAsync(
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        var maxBytes = _context.TryService<GraphQLResponseOptions>()?.MaxResponseBodyBytes ?? 10 * 1024 * 1024;
        while (true)
        {
            var message = await GraphQLWebSocketProtocol.ReceiveAsync(socket, maxBytes, cancellationToken)
                ?? throw new GraphQLProtocolException(
                    "The GraphQL WebSocket closed before acknowledging the connection.",
                    string.Empty);
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var typeNode) ? typeNode.GetString() : null;
            if (type == "connection_ack") return;
            if (type == "ping")
            {
                await GraphQLWebSocketProtocol.SendAsync(socket, new { type = "pong" }, cancellationToken);
                continue;
            }
            if (type is "connection_error" or "error")
                throw new GraphQLProtocolException(
                    "The GraphQL WebSocket rejected the connection.",
                    JsonDiagnosticSanitizer.Sanitize(message, _context.TryService<GraphQLAttachmentOptions>()));
            throw new GraphQLProtocolException(
                $"Expected a GraphQL WebSocket 'connection_ack' message, but received '{type ?? "<missing>"}'.",
                JsonDiagnosticSanitizer.Sanitize(message, _context.TryService<GraphQLAttachmentOptions>()));
        }
    }

    private void TraceSubscriptionStarted(GraphQLBuiltOperation operation, string transport, int? statusCode)
    {
        var attributes = new Dictionary<string, string?>
        {
            ["client.name"] = _targetName,
            ["graphql.operation.name"] = operation.Name,
            ["graphql.transport"] = transport
        };
        if (statusCode is not null) attributes["http.response.status_code"] = statusCode.Value.ToString();
        _context.Trace.WriteEvent(
            "graphql.subscription.start",
            $"GraphQL subscription · {operation.Name ?? "<anonymous>"}",
            "ProtoTest.GraphQL",
            outcome: ProtoTraceOutcome.Succeeded,
            attributes: attributes);
    }

    private static Uri ToWebSocketUri(Uri endpoint)
    {
        var builder = new UriBuilder(endpoint)
        {
            Scheme = endpoint.Scheme == Uri.UriSchemeHttps ? "wss" : "ws"
        };
        if (endpoint.IsDefaultPort) builder.Port = -1;
        return builder.Uri;
    }

    private GraphQLRequestBuilder Operation(string type, string? name, Action<GraphQLOperationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        ClearSimpleOperation();
        var builder = new GraphQLOperationBuilder(type, name);
        configure(builder);
        _operation = builder.Build();
        TraceConfiguration("graphql.operation.configure", $"Configure · {type} {name ?? "<anonymous>"}", new Dictionary<string, string?>()
        {
            ["graphql.operation.type"] = type,
            ["graphql.operation.name"] = name
        });
        return this;
    }

    private GraphQLRequestBuilder SimpleOperation(
        string type,
        string rootField,
        object? arguments,
        string? operationName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootField);
        _operation = null;
        _simpleOperationType = type;
        _simpleRootField = rootField;
        _simpleArguments = arguments;
        _simpleOperationName = operationName ?? char.ToUpperInvariant(rootField[0]) + rootField[1..];
        return this;
    }

    private GraphQLRequestBuilder BuildSimpleSelection(object? shape, Type shapeType)
    {
        var type = _simpleOperationType
            ?? throw new InvalidOperationException("Select is available after a shape-driven Query or Mutation.");
        var rootField = _simpleRootField!;
        var operation = new GraphQLOperationBuilder(type, _simpleOperationName);
        var variables = new Dictionary<string, object?>(StringComparer.Ordinal);
        operation.Field(rootField, field =>
        {
            GraphQLShapeSelection.AddArguments(operation, field, _simpleArguments, variables);
            GraphQLShapeSelection.Apply(field, shape, shapeType);
        });
        _operation = operation.Build();
        _variables = variables.Count == 0 ? null : variables;
        TraceConfiguration("graphql.operation.configure", $"Configure · {type} {_simpleOperationName ?? "<anonymous>"}",
            new Dictionary<string, string?>
            {
                ["graphql.operation.type"] = type,
                ["graphql.operation.name"] = _simpleOperationName,
                ["graphql.root.field"] = rootField,
                ["graphql.document.source"] = "shape"
            });
        return this;
    }

    private void ClearSimpleOperation()
    {
        _simpleOperationType = null;
        _simpleRootField = null;
        _simpleArguments = null;
        _simpleOperationName = null;
    }

    private void TraceConfiguration(string kind, string name, IReadOnlyDictionary<string, string?> attributes)
        => _context.Trace.WriteEvent(
            kind,
            name,
            "ProtoTest.GraphQL",
            outcome: ProtoTraceOutcome.Succeeded,
            attributes: attributes);

    private void TryRecordFailure(
        GraphQLBuiltOperation operation,
        string identifier,
        TimeSpan duration,
        Exception exception)
    {
        try
        {
            _context.RecordObservation(new ProtoObservation(
                _targetName,
                "graphql.failure",
                identifier,
                new GraphQLFailureData(
                    operation.Type,
                    operation.Name,
                    duration,
                    exception.GetType().FullName ?? exception.GetType().Name,
                    exception.Message)));
        }
        catch
        {
            // Diagnostics must never replace the original GraphQL failure.
        }
    }
}

public sealed record GraphQLResponseData(
    string OperationType,
    string? OperationName,
    string Document,
    int HttpStatusCode,
    int ErrorCount,
    IReadOnlyList<string> ErrorCodes,
    TimeSpan Duration,
    string? VariablesJson = null);

public sealed record GraphQLFailureData(
    string OperationType,
    string? OperationName,
    TimeSpan Duration,
    string ExceptionType,
    string Message);
