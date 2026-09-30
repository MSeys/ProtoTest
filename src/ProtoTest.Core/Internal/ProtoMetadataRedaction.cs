namespace ProtoTest.Core.Internal;

using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// The one evidence-boundary policy for report and trace metadata: sensitive names are redacted with
/// the shared list, reference cycles are replaced with a marker, containers are copied, and a leaf that
/// is not a JSON-safe scalar degrades to its formatted text. The result is a finite, cycle-free graph
/// that every sink and the archive can serialize with plain System.Text.Json without throwing.
/// </summary>
internal static class ProtoMetadataRedaction
{
    private const int MaxDepth = 16;

    /// <summary>The marker a reference cycle is replaced with.</summary>
    internal const string CircularValue = "[circular]";

    private static readonly HashSet<string> SensitiveNames =
        new(ProtoRedactionDefaults.SensitivePropertyNames, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyDictionary<string, object>? Redact(IReadOnlyDictionary<string, object>? metadata)
        => Redact(metadata, additionalSensitiveNames: null);

    /// <summary>
    /// Redacts metadata with the shared list plus the run's configured names, so a finding recorded
    /// on one host never carries a name another host configured.
    /// </summary>
    public static IReadOnlyDictionary<string, object>? Redact(
        IReadOnlyDictionary<string, object>? metadata,
        IReadOnlyCollection<string>? additionalSensitiveNames)
        => metadata is null || metadata.Count == 0
            ? metadata
            : RedactDictionary(
                metadata,
                EffectiveNames(additionalSensitiveNames),
                depth: 0,
                new HashSet<object>(ReferenceEqualityComparer.Instance));

    private static HashSet<string> EffectiveNames(IReadOnlyCollection<string>? additional)
    {
        if (additional is null || additional.Count == 0) return SensitiveNames;
        var names = new HashSet<string>(SensitiveNames, StringComparer.OrdinalIgnoreCase);
        foreach (var name in additional) names.Add(name);
        return names;
    }

    private static IReadOnlyDictionary<string, object> RedactDictionary(
        IEnumerable<KeyValuePair<string, object>> entries,
        HashSet<string> sensitive,
        int depth,
        HashSet<object> visited)
    {
        var redacted = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in entries)
        {
            redacted[key] = sensitive.Contains(key)
                ? ProtoUriSanitizer.RedactedValue
                : RedactValue(value, sensitive, depth + 1, visited)!;
        }

        return redacted;
    }

    private static object? RedactValue(
        object? value, HashSet<string> sensitive, int depth, HashSet<object> visited)
    {
        if (value is null || IsJsonSafeScalar(value))
        {
            return value;
        }

        if (depth > MaxDepth)
        {
            // Deep or exotic structures degrade to the formatter's bounded text instead of recursing.
            return ProtoTraceValueFormatter.Serialize(value, sensitive) ?? value.ToString();
        }

        if (value is IDictionary<string, object> or IReadOnlyDictionary<string, object>)
        {
            if (!visited.Add(value))
            {
                return CircularValue;
            }

            try
            {
                return RedactDictionary((IEnumerable<KeyValuePair<string, object>>)value, sensitive, depth, visited);
            }
            finally
            {
                visited.Remove(value);
            }
        }

        if (value is IEnumerable sequence and not string)
        {
            if (!visited.Add(value))
            {
                return CircularValue;
            }

            try
            {
                var changed = false;
                var items = new List<object?>();
                foreach (var item in sequence)
                {
                    var redacted = RedactValue(item, sensitive, depth + 1, visited);
                    changed |= !ReferenceEquals(redacted, item);
                    items.Add(redacted);
                }

                return changed ? items : value;
            }
            finally
            {
                visited.Remove(value);
            }
        }

        return ProtoTraceValueFormatter.Serialize(value, sensitive) ?? value.ToString();
    }

    private static bool IsJsonSafeScalar(object value)
        => value is string
            or char
            or bool
            or sbyte or byte or short or ushort or int or uint or long or ulong
            or float or double or decimal
            or DateTime or DateTimeOffset or TimeSpan or Guid or Uri
            or Enum
            or byte[]
            or JsonNode
            or JsonElement;
}
