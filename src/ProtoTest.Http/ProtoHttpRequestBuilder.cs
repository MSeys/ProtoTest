namespace ProtoTest.Http;

using ProtoTest.Core;

/// <summary>
/// The state and fluent surface every HTTP-based request builder shares: the client, context, target,
/// headers, authenticator resolution and base-address resolver, plus the hooks each protocol uses to
/// record how its authentication and headers were configured. A protocol's request shape (a REST
/// route, a GraphQL document) stays in the derived builder.
/// </summary>
public abstract class ProtoHttpRequestBuilder<TResponse, TBuilder>
    where TResponse : ProtoHttpResponse
    where TBuilder : ProtoHttpRequestBuilder<TResponse, TBuilder>
{
    private readonly ProtoProtocol _protocol;
    private readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);

    protected ProtoHttpRequestBuilder(
        HttpClient client,
        ProtoExecutionContext context,
        string targetName,
        ProtoProtocol protocol,
        string? clientEntityName = null)
    {
        Client = client ?? throw new ArgumentNullException(nameof(client));
        Context = context ?? throw new ArgumentNullException(nameof(context));
        TargetName = targetName ?? throw new ArgumentNullException(nameof(targetName));
        ClientEntityName = string.IsNullOrWhiteSpace(clientEntityName) ? TargetName : clientEntityName;
        _protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
    }

    /// <summary>The client the request is sent over.</summary>
    protected HttpClient Client { get; }

    /// <summary>The test execution context requests and traces belong to.</summary>
    protected ProtoExecutionContext Context { get; }

    /// <summary>The registered target name requests, observations and collectors agree on.</summary>
    protected string TargetName { get; }

    /// <summary>The registry key of the client the request actually uses; the client entity's id.</summary>
    protected string ClientEntityName { get; }

    /// <summary>The protocol identity: names, trace source, observation kinds and coverage category.</summary>
    protected ProtoProtocol Protocol => _protocol;

    /// <summary>The configured headers; mutable so a derived builder can compose them directly.</summary>
    protected Dictionary<string, string> Headers => _headers;

    /// <summary>The authenticator factory configured for this builder, if any.</summary>
    protected Func<ProtoExecutionContext, IProtoHttpAuthenticator>? AuthenticatorFactory { get; set; }

    /// <summary>The authenticator an earlier request resolved, reused so it is created once.</summary>
    protected IProtoHttpAuthenticator? ResolvedAuthenticator { get; set; }

    /// <summary>The per-test base-address resolver, when the client registered one.</summary>
    protected Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>>? BaseAddressResolver { get; set; }

    /// <summary>Applies the request's explicit authenticator; inherited authentication is replaced.</summary>
    public TBuilder Auth(IProtoHttpAuthenticator authenticator)
    {
        ArgumentNullException.ThrowIfNull(authenticator);
        ResolvedAuthenticator = authenticator;
        AuthenticatorFactory = _ => authenticator;
        OnAuthenticationConfigured("request", authenticator.GetType());
        return (TBuilder)this;
    }

    /// <summary>Applies an authenticator created through <see cref="ProtoAuthenticatorFactory"/>.</summary>
    public TBuilder Auth<TAuthenticator>(params object[] constructorArgs)
        where TAuthenticator : class, IProtoHttpAuthenticator
    {
        ResolvedAuthenticator = null;
        AuthenticatorFactory = context => ProtoAuthenticatorFactory.Create<TAuthenticator>(context, constructorArgs);
        OnAuthenticationConfigured("request", typeof(TAuthenticator));
        return (TBuilder)this;
    }

    /// <summary>Disables inherited or class-level authentication for this request builder.</summary>
    public TBuilder WithoutAuth()
    {
        ResolvedAuthenticator = null;
        AuthenticatorFactory = null;
        OnAuthenticationConfigured("request", authenticatorType: null);
        return (TBuilder)this;
    }

    /// <summary>
    /// Adopts the authenticator factory a protocol's lifecycle hook resolved for the test, so the
    /// builder resolves the same authenticators for its named client. A protocol integration calls this
    /// when it composes its request builder.
    /// </summary>
    /// <param name="authenticatorFactory">The factory the protocol resolved, or null for none.</param>
    public TBuilder UseAuthenticatorFactory(
        Func<ProtoExecutionContext, IProtoHttpAuthenticator>? authenticatorFactory)
    {
        ResolvedAuthenticator = null;
        AuthenticatorFactory = authenticatorFactory;
        return (TBuilder)this;
    }

    /// <summary>
    /// Adopts the per-test base-address resolver the resolved client registered, when it has one; a
    /// request builder without one uses the client's own <see cref="HttpClient.BaseAddress"/>.
    /// </summary>
    /// <param name="baseAddressResolver">The resolver the client resolution carries, or null for none.</param>
    public TBuilder UseBaseAddressResolver(
        Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>>? baseAddressResolver)
    {
        BaseAddressResolver = baseAddressResolver;
        return (TBuilder)this;
    }

    public TBuilder Header(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var isNewHeader = !_headers.ContainsKey(name);
        _headers[name] = value;
        OnHeaderConfigured(name, isNewHeader);
        return (TBuilder)this;
    }

    /// <summary>Resolves and applies authentication for one request, caching the authenticator.</summary>
    protected async ValueTask<IProtoHttpAuthenticator?> ApplyAuthenticationAsync(
        HttpRequestMessage request,
        ProtoTraceOperation? operation,
        CancellationToken cancellationToken)
    {
        ResolvedAuthenticator = await ProtoHttpAuthenticationApplier.ApplyAsync(
            AuthenticatorFactory,
            ResolvedAuthenticator,
            request,
            Context,
            TargetName,
            operation,
            cancellationToken);
        return ResolvedAuthenticator;
    }

    /// <summary>The response options this protocol registered.</summary>
    protected ProtoHttpResponseOptions ResolveResponseOptions()
        => Context.ResolveResponseOptions(_protocol.Key);

    /// <summary>Writes a successful configuration event under the protocol's trace source.</summary>
    protected void TraceConfiguration(
        string kind,
        string name,
        IReadOnlyDictionary<string, string?> attributes)
        => Context.Trace.WriteEvent(
            kind,
            name,
            _protocol.TraceSource,
            outcome: ProtoTraceOutcome.Succeeded,
            attributes: attributes);

    /// <summary>Records how authentication was configured; the protocol chooses attributes or events.</summary>
    protected abstract void OnAuthenticationConfigured(string source, Type? authenticatorType);

    /// <summary>Records a header configuration; <paramref name="isNewHeader"/> is false on replacement.</summary>
    protected virtual void OnHeaderConfigured(string name, bool isNewHeader)
    {
    }
}
