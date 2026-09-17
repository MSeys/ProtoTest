namespace ProtoTest.Core;

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Reflection;

/// <summary>
/// Serializes a value into compact, cycle-safe, redacted JSON for trace attributes. Shared so every
/// integration renders diagnostic values the same way.
/// </summary>
public static class ProtoTraceValueFormatter
{
    private const int MaximumLength = 64 * 1024;
    private static readonly HashSet<string> SensitiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "token", "access_token", "refresh_token", "secret", "apiKey", "api_key",
        "authorization", "cookie", "connectionString", "clientSecret"
    };

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        MaxDepth = 16,
        WriteIndented = false
    };

    public static string? Serialize(object? value)
    {
        if (value is null) return null;
        try
        {
            var json = JsonSerializer.Serialize(value, value.GetType(), SerializerOptions);
            var node = JsonNode.Parse(json);
            if (node is not null) Redact(node);
            var result = node?.ToJsonString(SerializerOptions) ?? json;
            return result.Length <= MaximumLength
                ? result
                : $"{result[..MaximumLength]}\n… [{result.Length - MaximumLength} characters truncated]";
        }
        catch (Exception)
        {
            var fallback = DescribeObject(value);
            Redact(fallback);
            return fallback.ToJsonString(SerializerOptions);
        }
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

    private static void Redact(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToArray())
            {
                if (SensitiveNames.Contains(property.Key)) obj[property.Key] = "[REDACTED]";
                else if (property.Value is not null) Redact(property.Value);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array) if (item is not null) Redact(item);
        }
    }
}
