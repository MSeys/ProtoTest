namespace ProtoTest.WireMock;

using System.Net;
using global::WireMock.ResponseBuilders;
using ProtoTest.Core;
using ProtoTest.WireMock.Internal;

/// <summary>
/// One stub on a fake: the method and path template it serves and the response it serves with. The
/// first response call registers the WireMock mapping; a later one replaces it, so re-stubbing never
/// stacks two mappings for one stub. Paths follow the WireMock path syntax, where <c>*</c> matches a
/// segment.
/// </summary>
public sealed class ProtoWireMockStub
{
    private readonly ProtoWireMockSession _session;
    private readonly ProtoExecutionContext _context;
    private readonly ProtoLock _gate = new();
    private Guid? _mappingId;
    private int? _statusCode;
    private Func<IResponseBuilder>? _responseFactory;
    private readonly Dictionary<string, string[]> _headers = new(StringComparer.OrdinalIgnoreCase);

    internal ProtoWireMockStub(
        ProtoWireMockSession session,
        ProtoExecutionContext context,
        string method,
        string path)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ValidatePath(path);
        _session = session;
        _context = context;
        Method = method.ToUpperInvariant();
        Path = path;
    }

    /// <summary>The HTTP method the stub serves, upper-cased.</summary>
    public string Method { get; }

    /// <summary>The path template the stub serves, as registered.</summary>
    public string Path { get; }

    /// <summary>Gets the stub's route identifier, the same shape the trace and coverage use.</summary>
    public string Identifier => ProtoWireMockProtocol.Identifier(Method, Path);

    /// <summary>Gets the status the stub currently serves, or null before the first response is set.</summary>
    public int? StatusCode
    {
        get
        {
            lock (_gate)
            {
                return _statusCode;
            }
        }
    }

    /// <summary>Gets how many requests this stub served; zero before the first response is set.</summary>
    public int ReceivedCount
    {
        get
        {
            Guid? mappingId;
            lock (_gate)
            {
                mappingId = _mappingId;
            }

            if (mappingId is null)
            {
                return 0;
            }

            return _session.Server.LogEntries.Count(entry => entry.MappingGuid == mappingId);
        }
    }

    /// <summary>Serves an empty body with the status.</summary>
    public ProtoWireMockStub RespondWith(HttpStatusCode statusCode)
    {
        _statusCode = (int)statusCode;
        _responseFactory = () => Response.Create().WithStatusCode((int)statusCode);
        Apply();
        return this;
    }

    /// <summary>Serves a JSON body with the status.</summary>
    public ProtoWireMockStub RespondJson(HttpStatusCode statusCode, object body)
    {
        ArgumentNullException.ThrowIfNull(body);
        _statusCode = (int)statusCode;
        _responseFactory = () => Response.Create().WithStatusCode((int)statusCode).WithBodyAsJson(body);
        Apply();
        return this;
    }

    /// <summary>Serves a raw body with the status and media type.</summary>
    public ProtoWireMockStub RespondWith(HttpStatusCode statusCode, string body, string mediaType)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        _statusCode = (int)statusCode;
        _responseFactory = () => Response.Create()
            .WithStatusCode((int)statusCode)
            .WithBody(body)
            .WithHeader("Content-Type", mediaType);
        Apply();
        return this;
    }

    /// <summary>Adds a response header to what the stub serves, replacing the mapping it registered.</summary>
    public ProtoWireMockStub WithHeader(string name, params string[] values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(values);
        lock (_gate)
        {
            _headers[name] = [.. values];
        }

        if (_responseFactory is not null)
        {
            Apply();
        }

        return this;
    }

    /// <summary>Fails naming the stub when it served no request.</summary>
    public ProtoWireMockStub VerifyHappened()
    {
        var received = ReceivedCount;
        if (received == 0)
        {
            throw new WireMockAssertionException(
                $"Expected stub {Identifier} on WireMock fake '{_session.FakeName}' to be requested, but it received no requests.");
        }

        return this;
    }

    /// <summary>Fails naming the stub when it did not serve exactly one request.</summary>
    public ProtoWireMockStub VerifyHappenedOnce() => VerifyHappened(1);

    /// <summary>Fails naming the stub when it did not serve exactly <paramref name="times"/> requests.</summary>
    public ProtoWireMockStub VerifyHappened(int times)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(times);
        var received = ReceivedCount;
        if (received != times)
        {
            throw new WireMockAssertionException(
                $"Expected stub {Identifier} on WireMock fake '{_session.FakeName}' to be requested " +
                $"{times} time(s), but it received {received}.");
        }

        return this;
    }

    private void Apply()
    {
        Func<IResponseBuilder> factory;
        Dictionary<string, string[]> headers;
        Guid previous;
        lock (_gate)
        {
            previous = _mappingId ?? Guid.Empty;
            factory = _responseFactory ?? throw new InvalidOperationException(
                $"Stub {Identifier} has no response; call RespondWith or RespondJson first.");
            headers = new Dictionary<string, string[]>(_headers, StringComparer.OrdinalIgnoreCase);
        }

        var mappingId = _session.ReplaceMapping(previous == Guid.Empty ? null : previous, Method, Path, () =>
        {
            var response = factory();
            foreach (var (name, values) in headers)
            {
                response.WithHeader(name, values);
            }

            return response;
        });

        lock (_gate)
        {
            _mappingId = mappingId;
        }
    }

    private static void ValidatePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!path.StartsWith("/", StringComparison.Ordinal))
        {
            throw new ArgumentException("A stub path must start with '/'.", nameof(path));
        }
    }
}
