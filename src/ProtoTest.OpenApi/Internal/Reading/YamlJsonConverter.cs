namespace ProtoTest.OpenApi.Internal.Reading;

using System.Text.Json.Nodes;
using YamlDotNet.RepresentationModel;

/// <summary>
/// Converts a YAML document into the JSON tree the document reader walks. Scalars stay strings: coverage
/// reads the document's structure, never a value's type. Aliases resolve to the node they name.
/// </summary>
internal static class YamlJsonConverter
{
    public static JsonNode? Convert(string yaml)
    {
        var stream = new YamlStream();
        using (var reader = new StringReader(yaml))
        {
            stream.Load(reader);
        }

        return stream.Documents.Count == 0 ? null : ToJson(stream.Documents[0].RootNode);
    }

    private static JsonNode? ToJson(YamlNode node) => node switch
    {
        YamlMappingNode mapping => ToObject(mapping),
        YamlSequenceNode sequence => new JsonArray([.. sequence.Children.Select(ToJson)]),
        YamlScalarNode scalar => scalar.Value is null ? null : JsonValue.Create(scalar.Value),
        _ => null
    };

    private static JsonObject ToObject(YamlMappingNode mapping)
    {
        var json = new JsonObject();
        foreach (var (key, value) in mapping.Children)
        {
            if (key is YamlScalarNode { Value: { } name })
            {
                json[name] = ToJson(value);
            }
        }

        return json;
    }
}
