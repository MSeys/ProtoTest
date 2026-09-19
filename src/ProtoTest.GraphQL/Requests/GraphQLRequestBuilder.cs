namespace ProtoTest.GraphQL;

using System.Collections;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    private IProtoHttpAuthenticator? _resolvedAuthenticator;
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
            response.ShouldMatchShape(expectedShape);
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
        var isNewHeader = !_headers.ContainsKey(name);
        _headers[name] = value;
        if (isNewHeader)
        {
            TraceConfiguration("http.header.configure", $"Header · {name}", new Dictionary<string, string?>()
            {
                ["http.header.name"] = name,
                ["http.header.value_recorded"] = "false"
            });
        }

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
        _resolvedAuthenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
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
        _resolvedAuthenticator = null;
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
        _resolvedAuthenticator = null;
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
        var operationScope = _context.Trace
            .Operation("graphql.operation", $"GraphQL · {identifier}", "ProtoTest.GraphQL")
            .For(ProtoTraceEntityKinds.Client, $"client:{typeof(HttpClient).FullName}:{_targetName}")
            .With("client.name", _targetName)
            .With("graphql.operation.type", operation.Type)
            .With("graphql.operation.name", operation.Name);
        if (_headers.Count > 0)
        {
            operationScope = operationScope.With("http.request.header_count", _headers.Count.ToString());
        }

        using var traceOperation = operationScope.Begin();
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
            if (!endpoint.IsAbsoluteUri || !ProtoHttpUri.IsHttpUri(endpoint))
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
        var requestContent = GraphQLRequestContent.Create(operation.DocumentText, operation.Name, _variables);
        var requestEnvelope = requestContent.DiagnosticJson;
        var variablesJson = requestContent.VariablesJson;
        request.Content = requestContent.Content;
        ApplyHeaders(request);
        if (requestContent.RequiresPreflight)
            request.Headers.TryAddWithoutValidation("GraphQL-preflight", "1");

        var attachmentOptions = _context.ResolveAttachmentOptions(ProtoGraphQLBuilder.ProtocolName);

        try
        {
            var requestNumber = attachmentOptions is null
                ? (int?)null
                : (_context.TryResolve<GraphQLContextState>() ?? throw new InvalidOperationException("GraphQL context state was not initialized.")).NextRequestNumber();
            var attachmentPrefix = requestNumber is null ? null : $"graphql-{requestNumber:00}";
            if (attachmentOptions?.CaptureRequestBodies == true)
                _context.AddAttachment(
                    $"{attachmentPrefix}-request",
                    JsonDiagnosticSanitizer.Sanitize(
                        GraphQLDocumentRedactor.RedactEnvelope(requestEnvelope, attachmentOptions),
                        attachmentOptions),
                    "application/json",
                    identifier);

            _resolvedAuthenticator = await ProtoHttpAuthenticationApplier.ApplyAsync(
                _authenticatorFactory,
                _resolvedAuthenticator,
                request,
                _context,
                _targetName,
                traceOperation,
                cancellationToken);

            HttpResponseMessage? rawResponse = null;
            try
            {
                rawResponse = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                await ProtoHttpResponseBuffer.BufferAsync(
                    rawResponse,
                    ResolveResponseOptions().MaxResponseBodyBytes,
                    cancellationToken);
                var content = await rawResponse.Content.ReadAsStringAsync(cancellationToken);
                stopwatch.Stop();
                var response = new GraphQLResponse(rawResponse, content, stopwatch.Elapsed, _context, _targetName, identifier, operation,
                    attachmentOptions, attachmentPrefix, traceOperation.Id, _simpleRootField);

                if (attachmentOptions?.CaptureResponses == true)
                    _context.AddAttachment(
                        $"{attachmentPrefix}-response",
                        JsonDiagnosticSanitizer.Sanitize(
                            GraphQLDocumentRedactor.Redact(content, attachmentOptions),
                            attachmentOptions),
                        rawResponse.Content.Headers.ContentType?.MediaType ?? "application/json",
                        identifier);

                _context.RecordObservation(new ProtoObservation(
                    _targetName,
                    "graphql.response",
                    identifier,
                    new GraphQLResponseData(
                        operation.Type,
                        operation.Name,
                        GraphQLDocumentRedactor.Redact(operation.DocumentText, attachmentOptions),
                        (int)rawResponse.StatusCode,
                        response.Errors.Count,
                        response.Errors.Select(error => error.Code).Where(code => code is not null).Cast<string>().ToArray(),
                        stopwatch.Elapsed,
                        variablesJson is null ? null : JsonDiagnosticSanitizer.Sanitize(variablesJson, attachmentOptions))));
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
        if (!endpoint.IsAbsoluteUri || !ProtoHttpUri.IsHttpUri(endpoint))
            throw new InvalidOperationException("A per-test GraphQL endpoint must be an absolute HTTP or HTTPS URI.");

        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        HttpResponseMessage? rawResponse = null;
        WebSocket? rawSocket = null;
        try
        {
            if (_subscriptionTransport == GraphQLSubscriptionTransport.Sse)
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            var requestContent = GraphQLRequestContent.Create(operation.DocumentText, operation.Name, _variables);
            request.Content = requestContent.Content;
            ApplyHeaders(request);
            if (requestContent.RequiresPreflight)
                request.Headers.TryAddWithoutValidation("GraphQL-preflight", "1");

            // A subscription has no request operation to attach the outcome to; an auth failure still
            // surfaces through the subscription's error path.
            _resolvedAuthenticator = await ProtoHttpAuthenticationApplier.ApplyAsync(
                _authenticatorFactory,
                _resolvedAuthenticator,
                request,
                _context,
                _targetName,
                requestOperation: null,
                cancellationToken);

            var attachmentOptions = _context.ResolveAttachmentOptions(ProtoGraphQLBuilder.ProtocolName);
            var requestNumber = attachmentOptions is null
                ? (int?)null
                : (_context.TryResolve<GraphQLContextState>()
                    ?? throw new InvalidOperationException("GraphQL context state was not initialized.")).NextRequestNumber();
            var attachmentPrefix = requestNumber is null ? null : $"graphql-{requestNumber:00}";
            if (attachmentOptions?.CaptureRequestBodies == true)
                _context.AddAttachment(
                    $"{attachmentPrefix}-request",
                    JsonDiagnosticSanitizer.Sanitize(
                        GraphQLDocumentRedactor.RedactEnvelope(requestContent.DiagnosticJson, attachmentOptions),
                        attachmentOptions),
                    "application/json",
                    identifier);

            var variablesJson = requestContent.VariablesJson is null
                ? null
                : JsonDiagnosticSanitizer.Sanitize(
                    requestContent.VariablesJson,
                    attachmentOptions);

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
                    ResolveResponseOptions().MaxResponseBodyBytes,
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
                ResolveResponseOptions().MaxResponseBodyBytes,
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
        var maxBytes = ResolveResponseOptions().MaxResponseBodyBytes;
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
                    JsonDiagnosticSanitizer.Sanitize(
                        message,
                        _context.ResolveAttachmentOptions(ProtoGraphQLBuilder.ProtocolName)));
            throw new GraphQLProtocolException(
                $"Expected a GraphQL WebSocket 'connection_ack' message, but received '{type ?? "<missing>"}'.",
                JsonDiagnosticSanitizer.Sanitize(
                    message,
                    _context.ResolveAttachmentOptions(ProtoGraphQLBuilder.ProtocolName)));
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
        // A shape that contributes no variables must not discard the ones the test set explicitly;
        // the shape's variables win on a name collision because the shape supplied them last.
        _variables = variables.Count == 0
            ? _variables
            : MergeVariables(_variables, variables);
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

    /// <summary>
    /// Merges the shape's variables over the explicitly configured ones so a shape that contributes
    /// none does not discard <c>Variables(...)</c>. The shape wins on a name collision.
    /// </summary>
    private static Dictionary<string, object?> MergeVariables(
        object? explicitVariables,
        IReadOnlyDictionary<string, object?> shapeVariables)
    {
        var merged = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (explicitVariables is not null)
        {
            foreach (var (name, value) in ReadVariables(explicitVariables))
                merged[name] = value;
        }

        foreach (var (name, value) in shapeVariables) merged[name] = value;
        return merged;
    }

    private static IEnumerable<(string Name, object? Value)> ReadVariables(object variables)
    {
        if (variables is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
                if (entry.Key is string key)
                    yield return (key, entry.Value);
            yield break;
        }

        // JSON-typed variables are enumerated as they will be serialized, so a shape can merge over
        // them without reinterpreting their CLR shape.
        if (variables is JsonElement element && element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
                yield return (property.Name, property.Value.Clone());
            yield break;
        }

        if (variables is JsonDocument document && document.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in document.RootElement.EnumerateObject())
                yield return (property.Name, property.Value.Clone());
            yield break;
        }

        if (variables is System.Text.Json.Nodes.JsonObject jsonObject)
        {
            foreach (var pair in jsonObject)
                yield return (pair.Key, pair.Value);
            yield break;
        }

        // Property names are normalized exactly as GraphQLRequestContent serializes them, so the merged
        // dictionary keeps the wire names of the explicit variables.
        foreach (var property in variables.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetIndexParameters().Length == 0))
        {
            var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            yield return (name, property.GetValue(variables));
        }
    }

    /// <summary>
    /// Adds the configured headers to the request, mirroring REST: a header that can be added to
    /// neither the request nor its content is an error rather than a silent drop.
    /// </summary>
    private void ApplyHeaders(HttpRequestMessage request)
    {
        foreach (var (name, value) in _headers)
        {
            if (!request.Headers.TryAddWithoutValidation(name, value)
                && (request.Content is null || !request.Content.Headers.TryAddWithoutValidation(name, value)))
            {
                throw new InvalidOperationException($"Header '{name}' could not be added to the GraphQL request.");
            }
        }
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

    private ProtoHttpResponseOptions ResolveResponseOptions()
        => _context.ResolveResponseOptions(ProtoGraphQLBuilder.ProtocolName);
}
