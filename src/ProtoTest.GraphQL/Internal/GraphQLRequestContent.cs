namespace ProtoTest.GraphQL.Internal;

using System.Collections;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

internal sealed record GraphQLRequestContent(
    HttpContent Content,
    string DiagnosticJson,
    string? VariablesJson,
    bool RequiresPreflight)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static GraphQLRequestContent Create(
        string document,
        string? operationName,
        object? variables)
    {
        var uploads = new List<(string Path, GraphQLUpload Upload)>();
        var normalizedVariables = Normalize(variables, "variables", uploads);
        var envelope = JsonSerializer.Serialize(
            new { query = document, operationName, variables = normalizedVariables },
            SerializerOptions);
        var variablesJson = variables is null
            ? null
            : JsonSerializer.Serialize(normalizedVariables, SerializerOptions);
        if (uploads.Count == 0)
            return new GraphQLRequestContent(
                new StringContent(envelope, Encoding.UTF8, "application/json"),
                envelope,
                variablesJson,
                RequiresPreflight: false);

        var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(envelope, Encoding.UTF8, "application/json"), "operations");
        var map = uploads.Select((upload, index) => new
        {
            Key = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Paths = new[] { upload.Path }
        }).ToDictionary(item => item.Key, item => item.Paths);
        multipart.Add(new StringContent(JsonSerializer.Serialize(map), Encoding.UTF8, "application/json"), "map");
        for (var index = 0; index < uploads.Count; index++)
        {
            var upload = uploads[index].Upload;
            var content = new StreamContent(upload.OpenRead());
            content.Headers.ContentType = new MediaTypeHeaderValue(upload.ContentType);
            multipart.Add(content, index.ToString(System.Globalization.CultureInfo.InvariantCulture), upload.FileName);
        }
        return new GraphQLRequestContent(multipart, envelope, variablesJson, RequiresPreflight: true);
    }

    private static object? Normalize(
        object? value,
        string path,
        ICollection<(string Path, GraphQLUpload Upload)> uploads)
    {
        if (value is GraphQLUpload upload)
        {
            uploads.Add((path, upload));
            return null;
        }
        if (value is null || IsScalar(value.GetType())
            || value is byte[] or JsonElement or JsonDocument or JsonNode)
            return value;
        if (value is IDictionary dictionary)
        {
            var result = new Dictionary<string, object?>();
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key is not string key)
                    throw new ArgumentException("GraphQL variable dictionaries must use string keys.");
                result[key] = Normalize(entry.Value, $"{path}.{key}", uploads);
            }
            return result;
        }
        if (value is IEnumerable sequence and not string)
        {
            var result = new List<object?>();
            var index = 0;
            foreach (var item in sequence)
                result.Add(Normalize(item, $"{path}.{index++}", uploads));
            return result;
        }

        var properties = new Dictionary<string, object?>();
        foreach (var property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(property => property.GetIndexParameters().Length == 0
                         && property.GetCustomAttribute<JsonIgnoreAttribute>() is null))
        {
            var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            properties[name] = Normalize(property.GetValue(value), $"{path}.{name}", uploads);
        }
        return properties;
    }

    private static bool IsScalar(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
            || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly)
            || type == typeof(TimeOnly) || type == typeof(Guid) || type == typeof(Uri);
    }
}
