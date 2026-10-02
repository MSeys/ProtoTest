namespace ProtoTest.OpenApi.Internal.Reading;

using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.Core;

/// <summary>Reads a document's text into one JSON tree, whether it was written as JSON or as YAML.</summary>
internal static class OpenApiTextReader
{
    public static JsonObject Read(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        try
        {
            var root = content.TrimStart().StartsWith('{')
                ? JsonNode.Parse(content)
                : YamlJsonConverter.Convert(content);
            return root as JsonObject
                ?? throw OpenApiReadException.Create("the document is not an object.");
        }
        catch (JsonException exception)
        {
            throw OpenApiReadException.Create(exception.Message, exception);
        }
        catch (YamlException exception)
        {
            throw OpenApiReadException.Create(exception.Message, exception);
        }
    }
}
