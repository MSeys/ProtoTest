namespace ProtoTest.GraphQL;

using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

internal static class GraphQLLiteral
{
    public static void Write(StringBuilder text, object? value)
    {
        switch (value)
        {
            case null: text.Append("null"); break;
            case GraphQLEnum enumeration: text.Append(enumeration.Value); break;
            case GraphQLVariableReference variable: text.Append('$').Append(variable.Name); break;
            case string stringValue: WriteString(text, stringValue); break;
            case bool boolean: text.Append(boolean ? "true" : "false"); break;
            case Enum enumeration: text.Append(enumeration.ToString().ToUpperInvariant()); break;
            case DateTime dateTime: WriteString(text, dateTime.ToString("O", CultureInfo.InvariantCulture)); break;
            case DateTimeOffset dateTimeOffset: WriteString(text, dateTimeOffset.ToString("O", CultureInfo.InvariantCulture)); break;
            case Guid guid: WriteString(text, guid.ToString()); break;
            case IDictionary dictionary:
                text.Append('{');
                var first = true;
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (entry.Key is not string key) throw new ArgumentException("GraphQL input object keys must be strings.");
                    if (!first) text.Append(", ");
                    first = false;
                    text.Append(key).Append(": ");
                    Write(text, entry.Value);
                }
                text.Append('}');
                break;
            case IEnumerable sequence:
                text.Append('[');
                first = true;
                foreach (var item in sequence)
                {
                    if (!first) text.Append(", ");
                    first = false;
                    Write(text, item);
                }
                text.Append(']');
                break;
            default:
                var type = value.GetType();
                if (type.IsPrimitive || value is decimal)
                {
                    text.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                }
                else
                {
                    text.Append('{');
                    first = true;
                    foreach (var property in type.GetProperties().Where(p => p.GetIndexParameters().Length == 0
                                 && p.GetCustomAttribute<JsonIgnoreAttribute>() is null))
                    {
                        if (!first) text.Append(", ");
                        first = false;
                        var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                            ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name);
                        text.Append(name).Append(": ");
                        Write(text, property.GetValue(value));
                    }
                    text.Append('}');
                }
                break;
        }
    }

    private static void WriteString(StringBuilder text, string value)
    {
        text.Append('"');
        foreach (var character in value)
        {
            text.Append(character switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => character.ToString()
            });
        }
        text.Append('"');
    }
}
