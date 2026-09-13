namespace ProtoTest.GraphQL;

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.GraphQL.Internal;
using ProtoTest.Json;

public sealed class GraphQLRequestBuilder
{
    private static readonly MediaTypeWithQualityHeaderValue GraphQLMediaType = new("application/graphql-response+json");
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _client;
    private readonly ProtoExecutionContext _context;
    private readonly string _targetName;
    private readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);
    private GraphQLBuiltOperation? _operation;
    private Func<ProtoExecutionContext, IGraphQLAuthenticator>? _authenticatorFactory;
    private IGraphQLAuthenticator? _authenticator;
    private object? _variables;
    private Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>>? _baseAddressResolver;

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

    public GraphQLRequestBuilder Request(string document, string? operationName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(document);
        var parsed = HotChocolate.Language.Utf8GraphQLParser.Parse(document);
        var operation = parsed.Definitions.OfType<HotChocolate.Language.OperationDefinitionNode>()
            .FirstOrDefault(definition => operationName is null || definition.Name?.Value == operationName)
            ?? throw new ArgumentException("The GraphQL document does not contain the requested operation.", nameof(document));
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

    public GraphQLRequestBuilder Auth(IGraphQLAuthenticator authenticator)
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
        where TAuthenticator : class, IGraphQLAuthenticator
    {
        _authenticator = null;
        _authenticatorFactory = context => Microsoft.Extensions.DependencyInjection.ActivatorUtilities
            .CreateInstance<TAuthenticator>(context.Services, constructorArgs);
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

    internal GraphQLRequestBuilder UseAuthenticatorFactory(Func<ProtoExecutionContext, IGraphQLAuthenticator>? factory)
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

    public async Task<GraphQLResponse> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var operation = _operation ?? throw new InvalidOperationException("Configure a query, mutation, or request before executing it.");
        var identifier = $"{operation.Type} {operation.Name ?? "<anonymous>"}";
        using var traceOperation = _context.Trace.StartOperation(
            "graphql.operation",
            $"GraphQL · {identifier}",
            "ProtoTest.GraphQL",
            attributes: new Dictionary<string, string?>
            {
                ["client.name"] = _targetName,
                ["graphql.operation.type"] = operation.Type,
                ["graphql.operation.name"] = operation.Name
            });
        var stopwatch = Stopwatch.StartNew();
        Uri endpoint;
        using var resolveOperation = _context.Trace.StartOperation(
            "graphql.endpoint.resolve",
            $"Resolve endpoint · {_targetName}",
            "ProtoTest.GraphQL",
            attributes: new Dictionary<string, string?> { ["client.name"] = _targetName });
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
        var requestEnvelope = JsonSerializer.Serialize(
            new { query = operation.DocumentText, operationName = operation.Name, variables = _variables },
            SerializerOptions);
        var variablesJson = _variables is null ? null : JsonSerializer.Serialize(_variables, SerializerOptions);
        request.Content = new StringContent(
            requestEnvelope,
            Encoding.UTF8,
            "application/json");

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
            if (_authenticatorFactory is not null)
            {
                using var authOperation = _context.Trace.StartOperation(
                    "auth.apply",
                    "Apply GraphQL authentication",
                    "ProtoTest.GraphQL",
                    attributes: new Dictionary<string, string?> { ["client.name"] = _targetName });
                try
                {
                    _authenticator ??= _authenticatorFactory(_context)
                        ?? throw new InvalidOperationException("The GraphQL authenticator factory returned null.");
                    authOperation.SetAttribute("auth.type", _authenticator.GetType().FullName);
                    await _authenticator.AuthenticateAsync(
                        new GraphQLAuthenticationContext(request, _context, _targetName), cancellationToken);
                    authOperation.Succeed();
                }
                catch (Exception exception)
                {
                    authOperation.Fail(exception);
                    throw;
                }
            }
            else
            {
                _context.Trace.WriteEvent(
                    "auth.skip",
                    "Authentication · None",
                    "ProtoTest.GraphQL",
                    outcome: ProtoTraceOutcome.Succeeded,
                    attributes: new Dictionary<string, string?> { ["client.name"] = _targetName });
            }

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
                    attachmentOptions, attachmentPrefix, traceOperation.Id);

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

    private GraphQLRequestBuilder Operation(string type, string? name, Action<GraphQLOperationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
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
