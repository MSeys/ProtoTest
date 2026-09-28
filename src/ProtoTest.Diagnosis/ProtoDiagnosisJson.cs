namespace ProtoTest.Diagnosis;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// The one JSON rendering of the diagnosis document and its context package: camel-case properties,
/// enum tokens in camel case, mismatches as the JSON values they were recorded with. The MCP tools and
/// the feedback channels share it, so the document has one wire shape.
/// </summary>
public static class ProtoDiagnosisJson
{
    private static readonly JsonSerializerOptions s_options = CreateOptions();

    /// <summary>Renders one run's diagnosis document.</summary>
    public static string ToJson(ProtoDiagnosisDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(document, s_options);
    }

    /// <summary>Renders one test's context package.</summary>
    public static string ToJson(ProtoDiagnosisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return JsonSerializer.Serialize(context, s_options);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
