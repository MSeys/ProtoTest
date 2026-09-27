namespace ProtoTest.WireMock.Internal;

using System.Collections;
using global::WireMock.Logging;
using global::WireMock.RequestBuilders;
using global::WireMock.ResponseBuilders;
using global::WireMock.Server;
using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.Rest;

/// <summary>
/// One fake's running server: per-test sessions are owned by the test that started them, per-run
/// sessions by the run. Starting binds the port and stopping releases it; a per-test session starts
/// empty, a per-run session keeps its stubs and log until the run releases it, and <c>Reset()</c>
/// clears them on demand. Reporting is by log index, so each request is recorded once however many
/// teardowns observe the shared server.
/// </summary>
internal sealed class ProtoWireMockSession : IProtoResource
{
    private readonly ProtoLock _gate = new();
    private readonly Dictionary<Guid, (string Method, string Path)> _stubs = new();
    private WireMockServer? _server;
    private int _reportedLogCount;
    private int _released;

    public ProtoWireMockSession(string fakeName, ProtoWireMockSettings settings, ProtoResourceScope scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fakeName);
        ArgumentNullException.ThrowIfNull(settings);
        FakeName = fakeName;
        Settings = settings;
        Scope = scope;
        TargetName = ProtoWireMockProtocol.TargetName(fakeName);
    }

    public string FakeName { get; }

    public ProtoWireMockSettings Settings { get; }

    public string TargetName { get; }

    public string Id => $"wiremock:{FakeName}";

    public string Kind => "wiremock";

    public string Description => $"WireMock fake '{FakeName}'";

    public ProtoResourceScope Scope { get; }

    public bool IsStarted
    {
        get
        {
            lock (_gate)
            {
                return _server is not null;
            }
        }
    }

    /// <summary>Gets the base URL the server listens on; empty until the server started.</summary>
    public string BaseUrl
    {
        get
        {
            lock (_gate)
            {
                return _server?.Url?.TrimEnd('/') ?? string.Empty;
            }
        }
    }

    /// <summary>Gets the bound port; zero until the server started.</summary>
    public int Port
    {
        get
        {
            lock (_gate)
            {
                return _server?.Ports.Count > 0 ? _server.Ports[0] : 0;
            }
        }
    }

    /// <summary>Gets the running server, or throws naming the fake when it never started.</summary>
    public WireMockServer Server
    {
        get
        {
            lock (_gate)
            {
                return _server ?? throw new InvalidOperationException(
                    $"WireMock fake '{FakeName}' is not running.");
            }
        }
    }

    /// <summary>
    /// Starts the server, reporting why it could not bind instead of surfacing the socket error raw.
    /// Starting twice is a no-op: the first start owns the server.
    /// </summary>
    public void Start(IProtoTraceWriter trace, string? scope)
    {
        ArgumentNullException.ThrowIfNull(trace);
        WireMockServer server;
        lock (_gate)
        {
            if (_server is not null)
            {
                return;
            }

            server = StartServer();
            _server = server;
        }

        var entityId = EntityId;
        trace.SetEntityState(
            ProtoTraceEntityKinds.Server,
            entityId,
            $"Fake · {FakeName}",
            new Dictionary<string, string?>
            {
                ["server.fake"] = FakeName,
                ["server.url"] = server.Url,
                ["server.lifetime"] = Settings.PerRun ? "run" : "test",
                ["server.state"] = "started"
            },
            scope: scope,
            change: "created");
        trace.WriteEvent(
            ProtoWireMockProtocol.StartOperation,
            $"Fake started · {FakeName}",
            ProtoWireMockProtocol.TraceSource,
            outcome: ProtoTraceOutcome.Succeeded,
            attributes: new Dictionary<string, string?>
            {
                ["server.fake"] = FakeName,
                ["server.url"] = server.Url,
                ["server.lifetime"] = Settings.PerRun ? "run" : "test"
            },
            entityKind: ProtoTraceEntityKinds.Server,
            entityId: entityId);
    }

    /// <summary>Stops the server once; a second stop is a no-op so run and test releases cannot double-stop.</summary>
    public void Stop(IProtoTraceWriter? trace, string? scope)
    {
        WireMockServer? server;
        lock (_gate)
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
            {
                return;
            }

            server = _server;
            _server = null;
            _stubs.Clear();
            _reportedLogCount = 0;
        }

        try
        {
            server?.Stop();
        }
        catch
        {
            // The release path must terminate: a stop failure is reported, not thrown, so teardown
            // still releases everything else the test owns.
            trace?.WriteEvent(
                ProtoWireMockProtocol.StopOperation,
                $"Fake stop failed · {FakeName}",
                ProtoWireMockProtocol.TraceSource,
                outcome: ProtoTraceOutcome.Failed,
                attributes: new Dictionary<string, string?>
                {
                    ["server.fake"] = FakeName
                },
                entityKind: ProtoTraceEntityKinds.Server,
                entityId: EntityId);
            return;
        }

        trace?.SetEntityState(
            ProtoTraceEntityKinds.Server,
            EntityId,
            $"Fake · {FakeName}",
            new Dictionary<string, string?>
            {
                ["server.fake"] = FakeName,
                ["server.state"] = "stopped"
            },
            scope: scope,
            change: "released");
    }

    public ValueTask ReleaseAsync(ProtoResourceReleaseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Stop(context.Trace, scope: null);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Clears the stubs and the request log now. A per-run fake keeps both for the whole run, so an
    /// explicit reset is how a suite clears shared state between its tests; release clears them with
    /// the run.
    /// </summary>
    public void Reset()
    {
        var server = Server;
        lock (_gate)
        {
            server.ResetMappings();
            server.ResetLogEntries();
            _stubs.Clear();
            _reportedLogCount = 0;
        }
    }

    /// <summary>
    /// Registers a facade stub's mapping, replacing its previous mapping when it re-stubs. The whole
    /// registration runs under the session lock, so the new mapping is exactly the one the diff finds
    /// even when parallel tests share a per-run server.
    /// </summary>
    internal Guid ReplaceMapping(
        Guid? previous,
        string method,
        string path,
        Func<IResponseBuilder> buildResponse)
    {
        ArgumentNullException.ThrowIfNull(buildResponse);
        lock (_gate)
        {
            var server = _server ?? throw new InvalidOperationException(
                $"WireMock fake '{FakeName}' is not running.");
            var known = server.Mappings.Select(mapping => mapping.Guid).ToHashSet();
            if (previous is { } replaced)
            {
                server.DeleteMapping(replaced);
                _stubs.Remove(replaced);
            }

            server.Given(Request.Create().WithPath(path).UsingMethod(method)).RespondWith(buildResponse());
            var fresh = server.Mappings
                .Select(mapping => mapping.Guid)
                .Where(guid => !known.Contains(guid))
                .ToArray();
            if (fresh.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Stub {ProtoWireMockProtocol.Identifier(method, path)} on WireMock fake " +
                    $"'{FakeName}' registered {fresh.Length} mappings; expected one.");
            }

            _stubs[fresh[0]] = (method, path);
            return fresh[0];
        }
    }

    /// <summary>
    /// Records every request since the last report as an observation on the test's trace: a matched
    /// request with the REST response shape, an unmatched one with the REST failure shape. Each entry
    /// is reported once; concurrent teardowns on a shared server split the new entries between them.
    /// </summary>
    public void ReportNewEntries(ProtoExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        List<ILogEntry> fresh;
        lock (_gate)
        {
            var server = _server;
            if (server is null)
            {
                return;
            }

            fresh = [.. server.LogEntries.Skip(_reportedLogCount)];
            _reportedLogCount = server.LogEntries.Count;
        }

        foreach (var entry in fresh)
        {
            ReportEntry(context, entry);
        }
    }

    private void ReportEntry(ProtoExecutionContext context, ILogEntry entry)
    {
        var request = entry.RequestMessage;
        var response = entry.ResponseMessage;
        if (request is null || response is null)
        {
            return;
        }

        var method = string.IsNullOrWhiteSpace(request.Method)
            ? "?"
            : request.Method.ToUpperInvariant();
        var actualPath = string.IsNullOrWhiteSpace(request.Path)
            ? "/"
            : request.Path;
        var duration = response.DateTime - request.DateTime;
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        var matched = entry.RequestMatchResult?.IsPerfectMatch == true;
        if (!matched)
        {
            var identifier = ProtoWireMockProtocol.Identifier(method, actualPath);
            context.RecordObservation(new ProtoObservation(
                TargetName,
                ProtoWireMockProtocol.FailureObservationKind,
                identifier,
                new RestFailureData(
                    method,
                    actualPath,
                    SanitizeUrl(request.Url),
                    duration,
                    "WireMockUnmatchedRequest",
                    $"No stub matched {identifier} on WireMock fake '{FakeName}'.",
                    false)));
            return;
        }

        string template;
        lock (_gate)
        {
            template = entry.MappingGuid is { } mappingId && _stubs.TryGetValue(mappingId, out var stub)
                ? stub.Path
                : actualPath;
        }

        var statusCode = ToStatusCode(response.StatusCode);
        context.RecordObservation(new ProtoObservation(
            TargetName,
            ProtoWireMockProtocol.ResponseObservationKind,
            ProtoWireMockProtocol.Identifier(method, template),
            new RestResponseData(
                method,
                template,
                statusCode,
                ProtoHttpDiagnosticSanitizer.SanitizeBody(ResponseBodyText(response), null),
                SanitizeHeaders(response.Headers),
                SanitizeUrl(request.Url),
                duration)));
    }

    private WireMockServer StartServer()
    {
        try
        {
            return Settings.Port is null
                ? WireMockServer.Start()
                : WireMockServer.Start(Settings.Port.Value);
        }
        catch (Exception exception)
        {
            var port = Settings.Port is null ? "a dynamic port" : $"port {Settings.Port}";
            throw new InvalidOperationException(
                $"WireMock fake '{FakeName}' could not start on {port}.", exception);
        }
    }

    private string EntityId => $"server:WireMock:{FakeName}";

    private static string ResponseBodyText(global::WireMock.IResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (!string.IsNullOrEmpty(response.BodyOriginal))
        {
            return response.BodyOriginal;
        }

        var body = response.BodyData;
        if (body is null)
        {
            return string.Empty;
        }

        // A JSON stub keeps the object it was given and serializes it when serving; serialize it the
        // same way for the observation instead of reporting the CLR ToString.
        if (body.BodyAsJson is string jsonText)
        {
            return jsonText;
        }

        if (body.BodyAsJson is not null)
        {
            if (body.BodyAsJson.GetType().FullName?.StartsWith(
                    "Newtonsoft.Json.Linq.", StringComparison.Ordinal) == true)
            {
                return body.BodyAsJson.ToString() ?? string.Empty;
            }

            try
            {
                return System.Text.Json.JsonSerializer.Serialize(body.BodyAsJson);
            }
            catch
            {
                return body.BodyAsJson.ToString() ?? string.Empty;
            }
        }

        if (body.BodyAsString is not null)
        {
            return body.BodyAsString;
        }

        return body.BodyAsBytes is { Length: > 0 } bytes
            ? System.Text.Encoding.UTF8.GetString(bytes)
            : string.Empty;
    }

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

    private static string? SanitizeUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        return ProtoHttpDiagnosticSanitizer.SanitizeUri(uri, null);
    }

    private static IReadOnlyDictionary<string, string> SanitizeHeaders(object? headers)
    {
        if (headers is not IDictionary dictionary || dictionary.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var pairs = new List<KeyValuePair<string, IEnumerable<string>>>();
        foreach (DictionaryEntry entry in dictionary)
        {
            if (entry.Key is not string name)
            {
                continue;
            }

            pairs.Add(new KeyValuePair<string, IEnumerable<string>>(name, HeaderValues(entry.Value)));
        }

        return ProtoHttpDiagnosticSanitizer.SanitizeHeaders(pairs, null);
    }

    private static IEnumerable<string> HeaderValues(object? value) => value switch
    {
        null => [],
        string single => [single],
        IEnumerable<string> strings => strings,
        IEnumerable enumerable => enumerable.Cast<object>().Select(item => item?.ToString() ?? string.Empty),
        _ => [value.ToString() ?? string.Empty]
    };
}
