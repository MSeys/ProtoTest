namespace ProtoTest.Core;

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

/// <summary>
/// Serializes a value into compact, cycle-safe, redacted JSON for trace attributes. Shared so every
/// integration renders diagnostic values the same way.
/// </summary>
public static class ProtoTraceValueFormatter
{
    private const int MaximumLength = 64 * 1024;

    // The one default list, shared with the JSON diagnostics axis (attachments and observations).
    private static readonly HashSet<string> SensitiveNames =
        new(ProtoRedactionDefaults.SensitivePropertyNames, StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        MaxDepth = 16,
        WriteIndented = false
    };

    public static string? Serialize(object? value)
        => Serialize(value, additionalSensitiveNames: null);

    /// <summary>
    /// Serializes a value into compact, cycle-safe, redacted JSON for trace attributes. Names in
    /// <paramref name="additionalSensitiveNames"/> redact like the defaults; the names travel with
    /// the host, so pass the run's configured ones and leave this empty where no host is in reach.
    /// </summary>
    public static string? Serialize(object? value, IReadOnlyCollection<string>? additionalSensitiveNames)
    {
        if (value is null) return null;
        var sensitive = EffectiveNames(additionalSensitiveNames);
        try
        {
            var json = JsonSerializer.Serialize(value, value.GetType(), SerializerOptions);
            var node = JsonNode.Parse(json);
            if (node is not null) Redact(node, sensitive);
            var result = node?.ToJsonString(SerializerOptions) ?? json;
            return result.Length <= MaximumLength
                ? result
                : $"{result[..MaximumLength]}\n… [{result.Length - MaximumLength} characters truncated]";
        }
        catch (Exception)
        {
            var fallback = DescribeObject(value);
            Redact(fallback, sensitive);
            return fallback.ToJsonString(SerializerOptions);
        }
    }

    private static HashSet<string> EffectiveNames(IReadOnlyCollection<string>? additional)
    {
        if (additional is null || additional.Count == 0) return SensitiveNames;
        var names = new HashSet<string>(SensitiveNames, StringComparer.OrdinalIgnoreCase);
        foreach (var name in additional) names.Add(name);
        return names;
    }

    private static JsonObject DescribeObject(object value)
    {
        var result = new JsonObject
        {
            ["$type"] = value.GetType().FullName,
            ["$diagnostic"] = "Serialized property-by-property because direct JSON serialization is unavailable for part of this object."
        };
        foreach (var property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(property => property.CanRead && property.GetIndexParameters().Length == 0))
        {
            try
            {
                var propertyValue = property.GetValue(value);
                result[property.Name] = DescribeValue(propertyValue);
            }
            catch (Exception exception)
            {
                result[property.Name] = new JsonObject
                {
                    ["$type"] = property.PropertyType.FullName,
                    ["$diagnostic"] = $"Getter failed: {exception.GetBaseException().Message}"
                };
            }
        }
        return result;
    }

    private static JsonNode? DescribeValue(object? value)
    {
        if (value is null) return null;
        if (value is Delegate callback)
        {
            return new JsonObject
            {
                ["$kind"] = "delegate",
                ["method"] = $"{callback.Method.DeclaringType?.FullName}.{callback.Method.Name}",
                ["targetType"] = callback.Target?.GetType().FullName
            };
        }
        if (value is Type type) return JsonValue.Create(type.FullName);
        try
        {
            return JsonSerializer.SerializeToNode(value, value.GetType(), SerializerOptions);
        }
        catch (Exception)
        {
            return new JsonObject
            {
                ["$type"] = value.GetType().FullName,
                ["$display"] = value.ToString(),
                ["$diagnostic"] = "Represented as display text because direct JSON serialization is unavailable."
            };
        }
    }

    private static void Redact(JsonNode node, HashSet<string> sensitive)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToArray())
            {
                if (sensitive.Contains(property.Key)) obj[property.Key] = "[REDACTED]";
                else if (property.Value is not null) Redact(property.Value, sensitive);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array) if (item is not null) Redact(item, sensitive);
        }
    }
}
