namespace ProtoTest.Json;

using System.Text.Json;

/// <summary>
/// How one protocol reads JSON: the exception its failures carry and the wording its required-read
/// reasons use. The read itself - parse, resolve the optional path, apply the required null rules and
/// deserialize - lives in <see cref="ProtoJsonRead"/>, so REST, GraphQL and messaging cannot drift.
/// </summary>
public sealed record ProtoJsonReadSemantics(
    Func<string, Exception> RequiredFailure,
    Func<string, Exception, Exception> PathMissFailure,
    string EmptyBodyReason,
    string NullBodyReason,
    Action<Exception, string?>? TraceFailure = null,
    bool NullRootReturnsDefault = false)
{
    /// <summary>The reason a required read names a JSON-null value at a path with.</summary>
    public static string NullValueReason(string jsonPath) => $"the value at '{jsonPath}' was JSON null";
}

/// <summary>
/// The shared JSON read: it parses the document, resolves the optional path, applies the required
/// null checks before the deserializer sees them and deserializes with the shared reader defaults. A
/// protocol keeps only its exception type and its trace vocabulary, supplied through
/// <see cref="ProtoJsonReadSemantics"/>; every failure is handed to that protocol's trace sink once.
/// </summary>
public static class ProtoJsonRead
{
    /// <summary>
    /// Reads <typeparamref name="T"/> from <paramref name="json"/> at an optional path. An empty
    /// document returns <c>default</c> unless <paramref name="required"/> is set, which fails with the
    /// semantics' empty-body reason. A required read also fails when the body, the value at the path or
    /// the deserialized result is JSON <c>null</c>. A path that does not resolve throws the semantics'
    /// path-miss exception, and a malformed document or a wrong-typed value throws the deserializer's
    /// <see cref="JsonException"/>.
    /// </summary>
    public static T? Read<T>(
        string? json,
        string? jsonPath,
        bool required,
        ProtoJsonReadSemantics semantics,
        JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(semantics);
        var hasPath = !string.IsNullOrWhiteSpace(jsonPath);
        if (string.IsNullOrWhiteSpace(json))
        {
            if (!required)
            {
                return default;
            }

            throw Fail(RequiredFailure<T>(jsonPath, semantics.EmptyBodyReason, semantics), jsonPath, semantics);
        }

        using var document = Parse(json, jsonPath, semantics);
        var element = document.RootElement;
        if (hasPath)
        {
            // A protocol whose root is the operation's data may have no data at all; that is the
            // nullable read's default, not a path miss.
            if (element.ValueKind == JsonValueKind.Null && semantics.NullRootReturnsDefault && !required)
            {
                return default;
            }

            try
            {
                element = JsonPathResolver.Resolve(element, jsonPath!);
            }
            catch (JsonPathException exception)
            {
                throw Fail(semantics.PathMissFailure(exception.Message, exception), jsonPath, semantics);
            }
        }

        if (required && element.ValueKind == JsonValueKind.Null)
        {
            throw Fail(
                RequiredFailure<T>(jsonPath, RequiredReason(hasPath, jsonPath, semantics), semantics),
                jsonPath,
                semantics);
        }

        T? value;
        try
        {
            value = element.Deserialize<T>(options ?? ProtoJsonDefaults.Reader);
        }
        catch (Exception exception)
        {
            throw Fail(exception, jsonPath, semantics);
        }

        if (required && value is null)
        {
            throw Fail(
                RequiredFailure<T>(jsonPath, RequiredReason(hasPath, jsonPath, semantics), semantics),
                jsonPath,
                semantics);
        }

        return value;
    }

    private static JsonDocument Parse(string json, string? jsonPath, ProtoJsonReadSemantics semantics)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (Exception exception)
        {
            throw Fail(exception, jsonPath, semantics);
        }
    }

    private static string RequiredReason(bool hasPath, string? jsonPath, ProtoJsonReadSemantics semantics)
        => hasPath ? ProtoJsonReadSemantics.NullValueReason(jsonPath!) : semantics.NullBodyReason;

    // The protocol's factory receives the full required-read message, so its exception type and subject
    // prefix stay the protocol's own.
    private static Exception RequiredFailure<T>(string? jsonPath, string reason, ProtoJsonReadSemantics semantics)
        => semantics.RequiredFailure($"{DescribeRequired<T>(jsonPath)} failed: {reason}.");

    /// <summary>
    /// The required-read name a failure message starts with, for example
    /// <c>ReadRequired&lt;Int32&gt;('$.id')</c>; public so a protocol that pre-checks (GraphQL's
    /// missing data) names the read the same way the shared helper does.
    /// </summary>
    public static string DescribeRequired<T>(string? jsonPath)
        => string.IsNullOrWhiteSpace(jsonPath)
            ? $"ReadRequired<{typeof(T).Name}>"
            : $"ReadRequired<{typeof(T).Name}>('{jsonPath}')";

    // The semantics build the protocol exception; the sink records it once, in the protocol's own
    // trace vocabulary, before the caller throws it.
    private static Exception Fail(Exception failure, string? jsonPath, ProtoJsonReadSemantics semantics)
    {
        semantics.TraceFailure?.Invoke(failure, jsonPath);
        return failure;
    }
}
