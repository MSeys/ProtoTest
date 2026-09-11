namespace ProtoTest.GraphQL;

using System.Collections;
using System.Globalization;
using System.Text;
using HotChocolate.Language;

public sealed class GraphQLOperationBuilder
{
    private readonly List<GraphQLFieldBuilder> _fields = [];
    private readonly List<(string Name, string Type)> _variables = [];
    private readonly string _operationType;

    internal GraphQLOperationBuilder(string operationType, string? name)
    {
        _operationType = operationType;
        Name = name;
    }

    public string? Name { get; }

    public GraphQLOperationBuilder Variable(string name, string type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        _variables.Add((name, type));
        return this;
    }

    public GraphQLOperationBuilder Variable(string name, GraphQLTypeReference type)
        => Variable(name, type?.Syntax ?? throw new ArgumentNullException(nameof(type)));

    public GraphQLOperationBuilder Field(string name, Action<GraphQLFieldBuilder>? configure = null)
    {
        var field = new GraphQLFieldBuilder(name);
        configure?.Invoke(field);
        _fields.Add(field);
        return this;
    }

    public GraphQLOperationBuilder Connection(string name, Action<GraphQLConnectionBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var connection = new GraphQLConnectionBuilder(name);
        configure(connection);
        _fields.Add(connection);
        return this;
    }

    internal GraphQLBuiltOperation Build()
    {
        if (_fields.Count == 0)
        {
            throw new InvalidOperationException("A GraphQL operation must select at least one field.");
        }

        var text = new StringBuilder(_operationType);
        if (!string.IsNullOrWhiteSpace(Name)) text.Append(' ').Append(Name);
        if (_variables.Count > 0)
        {
            text.Append('(');
            for (var index = 0; index < _variables.Count; index++)
            {
                if (index > 0) text.Append(", ");
                text.Append('$').Append(_variables[index].Name).Append(": ").Append(_variables[index].Type);
            }
            text.Append(')');
        }
        text.Append(" {");
        foreach (var field in _fields) field.Render(text, 1);
        text.AppendLine().Append('}');
        var document = Utf8GraphQLParser.Parse(text.ToString());
        return new GraphQLBuiltOperation(text.ToString(), document, _operationType, Name);
    }
}

public class GraphQLFieldBuilder
{
    private readonly List<(string Name, object? Value)> _arguments = [];
    private readonly List<GraphQLFieldBuilder> _fields = [];

    internal GraphQLFieldBuilder(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    internal string Name { get; }
    internal string? AliasName { get; private set; }

    public GraphQLFieldBuilder Alias(string alias)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        AliasName = alias;
        return this;
    }

    public GraphQLFieldBuilder Argument(string name, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _arguments.Add((name, value));
        return this;
    }

    public GraphQLFieldBuilder Select(Action<GraphQLSelectionBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var selection = new GraphQLSelectionBuilder(_fields);
        configure(selection);
        return this;
    }

    public GraphQLFieldBuilder Fields(params string[] names)
    {
        foreach (var name in names) _fields.Add(new GraphQLFieldBuilder(name));
        return this;
    }

    internal virtual void Render(StringBuilder text, int depth)
    {
        text.AppendLine().Append(' ', depth * 2);
        if (AliasName is not null) text.Append(AliasName).Append(": ");
        text.Append(Name);
        if (_arguments.Count > 0)
        {
            text.Append('(');
            for (var index = 0; index < _arguments.Count; index++)
            {
                if (index > 0) text.Append(", ");
                text.Append(_arguments[index].Name).Append(": ");
                GraphQLLiteral.Write(text, _arguments[index].Value);
            }
            text.Append(')');
        }
        if (_fields.Count > 0)
        {
            text.Append(" {");
            foreach (var field in _fields) field.Render(text, depth + 1);
            text.AppendLine().Append(' ', depth * 2).Append('}');
        }
    }
}

public sealed class GraphQLSelectionBuilder
{
    private readonly List<GraphQLFieldBuilder> _fields;
    internal GraphQLSelectionBuilder(List<GraphQLFieldBuilder> fields) => _fields = fields;

    public GraphQLSelectionBuilder Field(string name, Action<GraphQLFieldBuilder>? configure = null)
    {
        var field = new GraphQLFieldBuilder(name);
        configure?.Invoke(field);
        _fields.Add(field);
        return this;
    }

    public GraphQLSelectionBuilder Fields(params string[] names)
    {
        foreach (var name in names) _fields.Add(new GraphQLFieldBuilder(name));
        return this;
    }
}

public sealed class GraphQLConnectionBuilder : GraphQLFieldBuilder
{
    internal GraphQLConnectionBuilder(string name) : base(name) { }

    public GraphQLConnectionBuilder First(int count) { Argument("first", count); return this; }
    public GraphQLConnectionBuilder After(string cursor) { Argument("after", cursor); return this; }
    public GraphQLConnectionBuilder Last(int count) { Argument("last", count); return this; }
    public GraphQLConnectionBuilder Before(string cursor) { Argument("before", cursor); return this; }

    public GraphQLConnectionBuilder Where(Action<GraphQLFilterBuilder> configure)
    {
        var filter = new GraphQLFilterBuilder();
        configure(filter);
        Argument("where", filter.Value);
        return this;
    }

    public GraphQLConnectionBuilder OrderBy(Action<GraphQLOrderBuilder> configure)
    {
        var order = new GraphQLOrderBuilder();
        configure(order);
        Argument("order", order.Value);
        return this;
    }

    public GraphQLConnectionBuilder Nodes(Action<GraphQLFieldBuilder> configure)
    {
        Select(selection => selection.Field("nodes", configure));
        return this;
    }

    public GraphQLConnectionBuilder Nodes(params string[] fields)
        => Nodes(nodes => nodes.Fields(fields));

    public GraphQLConnectionBuilder PageInfo(params string[] fields)
    {
        if (fields.Length == 0) fields = ["hasNextPage", "hasPreviousPage", "startCursor", "endCursor"];
        Select(selection => selection.Field("pageInfo", page => page.Fields(fields)));
        return this;
    }

    public GraphQLConnectionBuilder TotalCount()
    {
        Select(selection => selection.Field("totalCount"));
        return this;
    }
}

public sealed class GraphQLFilterBuilder
{
    private readonly Dictionary<string, object?> _fields = new(StringComparer.Ordinal);
    internal IReadOnlyDictionary<string, object?> Value => _fields;

    public GraphQLFilterBuilder Equal(string field, object? value) => Operation(field, "eq", value);
    public GraphQLFilterBuilder NotEqual(string field, object? value) => Operation(field, "neq", value);
    public GraphQLFilterBuilder Contains(string field, string value) => Operation(field, "contains", value);
    public GraphQLFilterBuilder StartsWith(string field, string value) => Operation(field, "startsWith", value);
    public GraphQLFilterBuilder EndsWith(string field, string value) => Operation(field, "endsWith", value);
    public GraphQLFilterBuilder GreaterThan(string field, object value) => Operation(field, "gt", value);
    public GraphQLFilterBuilder GreaterThanOrEqual(string field, object value) => Operation(field, "gte", value);
    public GraphQLFilterBuilder LessThan(string field, object value) => Operation(field, "lt", value);
    public GraphQLFilterBuilder LessThanOrEqual(string field, object value) => Operation(field, "lte", value);
    public GraphQLFilterBuilder In(string field, params object?[] values) => Operation(field, "in", values);

    public GraphQLFilterBuilder Some(string field, Action<GraphQLFilterBuilder> configure)
    {
        var nested = new GraphQLFilterBuilder();
        configure(nested);
        _fields[field] = new Dictionary<string, object?> { ["some"] = nested.Value };
        return this;
    }

    public GraphQLFilterBuilder Nested(string field, Action<GraphQLFilterBuilder> configure)
    {
        var nested = new GraphQLFilterBuilder();
        configure(nested);
        _fields[field] = nested.Value;
        return this;
    }

    public GraphQLFilterBuilder Or(params Action<GraphQLFilterBuilder>[] alternatives)
    {
        _fields["or"] = alternatives.Select(action =>
        {
            var item = new GraphQLFilterBuilder();
            action(item);
            return item.Value;
        }).ToArray();
        return this;
    }

    private GraphQLFilterBuilder Operation(string field, string operation, object? value)
    {
        if (!_fields.TryGetValue(field, out var existing) || existing is not Dictionary<string, object?> operations)
        {
            operations = new Dictionary<string, object?>(StringComparer.Ordinal);
            _fields[field] = operations;
        }
        operations[operation] = value;
        return this;
    }
}

public sealed class GraphQLOrderBuilder
{
    private readonly List<IReadOnlyDictionary<string, object?>> _items = [];
    internal IReadOnlyList<IReadOnlyDictionary<string, object?>> Value => _items;
    public GraphQLOrderBuilder Ascending(string field) { _items.Add(new Dictionary<string, object?> { [field] = Gql.Enum("ASC") }); return this; }
    public GraphQLOrderBuilder Descending(string field) { _items.Add(new Dictionary<string, object?> { [field] = Gql.Enum("DESC") }); return this; }
}

public static class Gql
{
    public static GraphQLEnum Enum(string value) => new(value);
    public static GraphQLVariableReference Var(string name) => new(name);
}

public sealed record GraphQLEnum(string Value);
public sealed record GraphQLVariableReference(string Name);

public sealed record GraphQLTypeReference(string Syntax)
{
    public GraphQLTypeReference NonNull() => new($"{Syntax}!");
    public GraphQLTypeReference List() => new($"[{Syntax}]");
}

public static class GqlType
{
    public static GraphQLTypeReference Id { get; } = new("ID");
    public static GraphQLTypeReference String { get; } = new("String");
    public static GraphQLTypeReference Int { get; } = new("Int");
    public static GraphQLTypeReference Float { get; } = new("Float");
    public static GraphQLTypeReference Boolean { get; } = new("Boolean");
    public static GraphQLTypeReference Named(string name) => new(name);
}

internal sealed record GraphQLBuiltOperation(string DocumentText, DocumentNode Document, string Type, string? Name);

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
                    foreach (var property in type.GetProperties().Where(p => p.GetIndexParameters().Length == 0))
                    {
                        if (!first) text.Append(", ");
                        first = false;
                        var name = char.ToLowerInvariant(property.Name[0]) + property.Name[1..];
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
