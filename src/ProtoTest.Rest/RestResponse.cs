namespace ProtoTest.Rest;

using ProtoTest.Core;
using ProtoTest.Rest.Matching;
using System.Dynamic;
using System.Collections;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

public sealed class RestResponse(
    HttpResponseMessage rawResponse,
    string content,
    TimeSpan elapsedTime,
    ProtoExecutionContext? context = null,
    string? targetName = null,
    string? routeIdentifier = null,
    RestAttachmentOptions? attachmentOptions = null,
    string? attachmentPrefix = null
)
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

    public T? ReadAsJson<T>(JsonSerializerOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(Content))
            return default;

        return JsonSerializer.Deserialize<T>(Content, options ?? DefaultJsonOptions);
    }

    public T? ReadAsAnonymous<T>(T anonymousTypeDefinition, JsonSerializerOptions? options = null)
    {
        return ReadAsJson<T>(options);
    }

    public dynamic? ReadAsDynamic()
    {
        if (string.IsNullOrWhiteSpace(Content))
            return null;

        using var doc = JsonDocument.Parse(Content);
        return ConvertJsonElement(doc.RootElement.Clone());
    }

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

    public RestResponse ShouldMatchShape(object expectedShape)
    {
        ArgumentNullException.ThrowIfNull(expectedShape);

        if (context is not null && attachmentOptions?.CaptureExpectedShapes == true)
        {
            context.AddAttachment(
                $"{attachmentPrefix}-expected-shape",
                JsonSerializer.Serialize(
                    DescribeExpectedValue(expectedShape),
                    new JsonSerializerOptions { WriteIndented = true }),
                "application/json",
                routeIdentifier);
        }

        var matchedProps = ShapeMatcher.AssertMatch(Content, expectedShape);

        // 🎯 Record ShapeMatch Hit if context is available
        if (context != null && !string.IsNullOrEmpty(targetName) && !string.IsNullOrEmpty(routeIdentifier))
        {
            context.RecordHit(new CoverageHit(
                TargetName: targetName,
                Identifier: routeIdentifier,
                Data: new ShapeMatchData(
                    RouteTemplate: routeIdentifier,
                    MatchedProperties: matchedProps,
                    TargetType: expectedShape.GetType()
                )
            ));
        }

        return this;
    }

    private static object? DescribeExpectedValue(object? expected)
    {
        if (expected is null)
        {
            return null;
        }

        if (expected is IValueMatcher matcher)
        {
            return $"constraint: {matcher.Description}";
        }

        var type = expected.GetType();
        if (type.IsPrimitive || type.IsEnum || expected is string or decimal or DateTime or DateTimeOffset or Guid)
        {
            return expected;
        }

        if (expected is IEnumerable values)
        {
            return values.Cast<object?>().Select(DescribeExpectedValue).ToArray();
        }

        return type.GetProperties()
            .Where(property => property.GetIndexParameters().Length == 0)
            .ToDictionary(
                property => property.Name,
                property => DescribeExpectedValue(property.GetValue(expected)));
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
