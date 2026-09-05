namespace ProtoTest.Rest;

using ProtoTest.Rest.Matching;
using System.Dynamic;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

public sealed class RestResponse(HttpResponseMessage rawResponse, string content, TimeSpan elapsedTime)
{
    private static readonly JsonSerializerOptions DefaultJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public HttpResponseMessage RawResponse { get; } = rawResponse ?? throw new ArgumentNullException(nameof(rawResponse));
    public HttpStatusCode StatusCode => RawResponse.StatusCode;
    public bool IsSuccessStatusCode => RawResponse.IsSuccessStatusCode;
    public HttpResponseHeaders Headers => RawResponse.Headers;
    public TimeSpan ElapsedTime { get; } = elapsedTime;
    public string Content { get; } = content ?? string.Empty;

    /// <summary>
    /// Deserializes the JSON body into a strongly-typed object.
    /// </summary>
    public T? ReadAsJson<T>(JsonSerializerOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(Content))
            return default;

        return JsonSerializer.Deserialize<T>(Content, options ?? DefaultJsonOptions);
    }

    /// <summary>
    /// Deserializes the JSON body using the shape of an anonymous object.
    /// Usage: response.ReadAsAnonymous(new { id = 0, name = "" })
    /// </summary>
    public T? ReadAsAnonymous<T>(T anonymousTypeDefinition, JsonSerializerOptions? options = null)
    {
        return ReadAsJson<T>(options);
    }

    /// <summary>
    /// Deserializes the JSON body as a dynamic JsonElement/ExpandoObject structure.
    /// </summary>
    public dynamic? ReadAsDynamic()
    {
        if (string.IsNullOrWhiteSpace(Content))
            return null;

        using var doc = JsonDocument.Parse(Content);
        return ConvertJsonElement(doc.RootElement.Clone());
    }

    /// <summary>
    /// Asserts that the response HTTP status code matches the expected status code.
    /// </summary>
    public RestResponse ShouldHaveStatus(HttpStatusCode expectedStatusCode)
    {
        if (StatusCode != expectedStatusCode)
        {
            throw new InvalidOperationException(
                $"Expected HTTP Status {(int)expectedStatusCode} ({expectedStatusCode}), " +
                $"but received {(int)StatusCode} ({StatusCode}). Response Body:\n{Content}");
        }
        return this;
    }

    // <summary>
    /// Asserts that the JSON response structure and values match the provided shape specification.
    /// Partial matching is supported: properties not specified in expectedShape are ignored.
    /// </summary>
    public RestResponse ShouldMatchShape(object expectedShape)
    {
        ArgumentNullException.ThrowIfNull(expectedShape);
        ShapeMatcher.AssertMatch(Content, expectedShape);
        return this; // Returns itself for chaining!
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .Aggregate(new ExpandoObject() as IDictionary<string, object?>, (acc, prop) =>
                {
                    acc[prop.Name] = ConvertJsonElement(prop.Value);
                    return acc;
                }),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }
}