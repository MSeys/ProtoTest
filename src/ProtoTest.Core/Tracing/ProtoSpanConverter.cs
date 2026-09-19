namespace ProtoTest.Core;

using System.Collections.Concurrent;
using System.Diagnostics;

/// <summary>
/// Materializes tracked state from ordinary OpenTelemetry spans. An identity-shaped attribute on a span
/// (<c>project.id</c>, <c>invoice.number</c>) makes that span one version of the item; the other attributes
/// sharing its prefix become the item's state. First sight is <c>created</c>, a different state is
/// <c>changed</c>, a repeat is <c>observed</c>.
/// <para>
/// The application carries no ProtoTest vocabulary and no per-application mapper exists: the converter
/// reads what plain instrumentation already says, the same way an OpenTelemetry backend maps standard
/// semantic conventions.
/// </para>
/// </summary>
internal sealed class ProtoSpanConverter
{
    private static readonly string[] IdentitySuffixes = [".id", ".number", ".key"];

    // Prefixes owned by telemetry stacks, frameworks and correlation: never a domain item.
    private static readonly HashSet<string> IgnoredPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "activity", "cloud", "client", "container", "correlation", "db", "event", "exception", "faas",
        "graphql", "host", "http", "k8s", "log", "messaging", "net", "os", "process", "request", "rpc",
        "server", "service", "session", "span", "telemetry", "thread", "trace", "url", "user"
    };

    private readonly ConcurrentDictionary<IProtoTraceWriter, ConcurrentDictionary<string, string>> _states = new();

    /// <summary>Gets the number of writers whose observed state is still tracked; used by tests.</summary>
    internal int TrackedWriterCount => _states.Count;

    /// <summary>
    /// Forgets a writer's observed state when its recorder completes, so the map cannot grow with every
    /// test a long-lived host runs. A completed recorder never observes another span.
    /// </summary>
    public void Forget(IProtoTraceWriter writer) => _states.TryRemove(writer, out _);

    public void Observe(IProtoTraceWriter writer, Activity activity, string? operationId)
    {
        foreach (var identity in Identities(activity))
        {
            var state = ReadState(activity, identity.Prefix, identity.Key);
            var itemKey = $"{identity.Prefix}:{identity.Value}";
            var known = _states.GetOrAdd(
                writer,
                static _ => new ConcurrentDictionary<string, string>(StringComparer.Ordinal));
            string change;
            var serialized = Serialize(state);
            lock (known)
            {
                change = !known.TryGetValue(itemKey, out var previous)
                    ? "created"
                    : string.Equals(previous, serialized, StringComparison.Ordinal) ? "observed" : "changed";
                known[itemKey] = serialized;
            }

            writer.Value(
                "value",
                $"{identity.Prefix}:{identity.Value}",
                $"{Title(identity.Prefix)} {identity.Value}",
                change,
                state,
                ProtoTraceValueSource.ApplicationSide,
                operationId: operationId);
        }
    }

    private static IEnumerable<(string Prefix, string Key, string Value)> Identities(Activity activity)
    {
        foreach (var tag in activity.TagObjects)
        {
            var suffix = IdentitySuffixes.FirstOrDefault(
                candidate => tag.Key.EndsWith(candidate, StringComparison.OrdinalIgnoreCase));
            if (suffix is null)
            {
                continue;
            }

            var prefix = tag.Key[..^suffix.Length];
            if (prefix.Length == 0 || prefix.Contains('.') || IgnoredPrefixes.Contains(prefix))
            {
                continue;
            }

            var value = tag.Value?.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return (prefix, tag.Key, value);
            }
        }
    }

    private static Dictionary<string, string?> ReadState(Activity activity, string prefix, string identityKey)
    {
        var state = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var tag in activity.TagObjects)
        {
            if (tag.Key == identityKey
                || !tag.Key.StartsWith($"{prefix}.", StringComparison.OrdinalIgnoreCase)
                || tag.Key.StartsWith("prototest.", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            state[tag.Key] = tag.Value?.ToString();
        }

        return state;
    }

    private static string Serialize(IReadOnlyDictionary<string, string?> state)
        => string.Join(
            "\n",
            state.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}={pair.Value}"));

    private static string Title(string prefix)
        => char.ToUpperInvariant(prefix[0]) + prefix[1..];
}
