namespace ProtoTest.GraphQL;

using System.Text;
using HotChocolate.Language;

public sealed class GraphQLOperationBuilder
{
    private readonly List<GraphQLFieldBuilder> _fields = [];
    private readonly List<(string Name, string Type)> _variables = [];
    private readonly GraphQLOperationKind _kind;

    internal GraphQLOperationBuilder(GraphQLOperationKind kind, string? name)
    {
        _kind = kind;
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

        var text = new StringBuilder(_kind.WireName());
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
        return new GraphQLBuiltOperation(text.ToString(), _kind, Name);
    }
}
