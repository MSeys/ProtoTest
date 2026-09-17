namespace ProtoTest.GraphQL;

using System.Text;

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
