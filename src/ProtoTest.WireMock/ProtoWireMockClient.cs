namespace ProtoTest.WireMock;

using global::WireMock.Matchers.Request;
using global::WireMock.ResponseBuilders;
using global::WireMock.Server;
using ProtoTest.Core;
using ProtoTest.WireMock.Internal;

/// <summary>
/// One fake HTTP service for the running test: stub routes that read like the scenario, the base URL
/// to point the system under test at, and the requests the fake served. The fake starts on the first
/// call that needs it; a per-test fake stops with the test, a per-run fake keeps serving with its
/// stubs and log until the run releases it.
/// </summary>
public sealed class ProtoWireMockClient
{
    private readonly ProtoExecutionContext _context;

    internal ProtoWireMockClient(ProtoWireMockSession session, ProtoExecutionContext context)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>The session behind this client: the server, its stubs and its request log.</summary>
    internal ProtoWireMockSession Session { get; }

    /// <summary>The fake's name, as registered with <c>AddWireMock</c>.</summary>
    public string Name => Session.FakeName;

    /// <summary>The base URL the fake listens on, without a trailing slash.</summary>
    public string BaseUrl => Session.BaseUrl;

    /// <summary>The port the fake listens on.</summary>
    public int Port => Session.Port;

    /// <summary>
    /// The running WireMock server, for matchers the facade does not cover. Requests it serves are
    /// still observed in the trace, with their concrete path as the identifier.
    /// </summary>
    public WireMockServer Server => Session.Server;

    /// <summary>
    /// Declares a stub the fake serves: the method and path template requests must match. Paths follow
    /// the WireMock path syntax, where <c>*</c> matches a segment. The stub serves nothing until a
    /// response is set on it; registering it records the stub for coverage immediately.
    /// </summary>
    public ProtoWireMockStub Stub(HttpMethod method, string path)
    {
        ArgumentNullException.ThrowIfNull(method);
        return Stub(method.Method, path);
    }

    /// <summary>
    /// Declares a stub the fake serves: the method and path template requests must match. Paths follow
    /// the WireMock path syntax, where <c>*</c> matches a segment. The stub serves nothing until a
    /// response is set on it; registering it records the stub for coverage immediately.
    /// </summary>
    public ProtoWireMockStub Stub(string method, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        var stub = new ProtoWireMockStub(Session, _context, method, path);
        _context.RecordObservation(new ProtoObservation(
            Session.TargetName,
            ProtoWireMockProtocol.StubObservationKind,
            stub.Identifier,
            new WireMockStubData(stub.Method, stub.Path)));
        return stub;
    }

    /// <summary>
    /// Registers a raw WireMock matcher the facade does not cover. The match is still observed in the
    /// trace, but without a route template the identifier is the concrete request path.
    /// </summary>
    public IRespondWithAProvider Given(IRequestMatcher matcher)
    {
        ArgumentNullException.ThrowIfNull(matcher);
        return Session.Server.Given(matcher);
    }

    /// <summary>Gets every request the fake served, oldest first.</summary>
    public IReadOnlyList<ProtoWireMockRequest> ReceivedRequests
    {
        get
        {
            var requests = new List<ProtoWireMockRequest>();
            foreach (var entry in Session.Server.LogEntries)
            {
                var request = entry.RequestMessage;
                var response = entry.ResponseMessage;
                if (request is null || response is null)
                {
                    continue;
                }

                var method = string.IsNullOrWhiteSpace(request.Method)
                    ? "?"
                    : request.Method.ToUpperInvariant();
                var path = string.IsNullOrWhiteSpace(request.Path)
                    ? "/"
                    : request.Path;
                requests.Add(new ProtoWireMockRequest(
                    method,
                    path,
                    entry.RequestMatchResult?.IsPerfectMatch == true,
                    ToStatusCode(response.StatusCode)));
            }

            return requests;
        }
    }

    /// <summary>Gets the requests no stub matched, oldest first.</summary>
    public IReadOnlyList<ProtoWireMockRequest> UnmatchedRequests =>
        [.. ReceivedRequests.Where(request => !request.Matched)];

    /// <summary>
    /// Fails naming the fake and every unmatched request when the fake served one. Call it at the end
    /// of a test that must only hit stubbed routes.
    /// </summary>
    public void VerifyNoUnmatchedRequests()
    {
        var unmatched = UnmatchedRequests;
        if (unmatched.Count == 0)
        {
            return;
        }

        throw new WireMockAssertionException(
            $"WireMock fake '{Name}' received {unmatched.Count} unmatched request(s): " +
            string.Join(", ", unmatched.Select(request => $"{request.Method} {request.Path}")) + ".");
    }

    /// <summary>Clears the stubs and the request log now, instead of waiting for the run's release.</summary>
    public void Reset() => Session.Reset();

    private static int ToStatusCode(object? statusCode)
    {
        try
        {
            return Convert.ToInt32(statusCode);
        }
        catch
        {
            return 0;
        }
    }
}
