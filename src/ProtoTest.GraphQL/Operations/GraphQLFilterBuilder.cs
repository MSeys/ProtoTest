namespace ProtoTest.GraphQL;

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
        _fields[field] = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["some"] = Build(configure)
        };
        return this;
    }

    public GraphQLFilterBuilder Nested(string field, Action<GraphQLFilterBuilder> configure)
    {
        _fields[field] = Build(configure);
        return this;
    }

    public GraphQLFilterBuilder Or(params Action<GraphQLFilterBuilder>[] alternatives)
    {
        _fields["or"] = alternatives.Select(Build).ToArray();
        return this;
    }

    private static IReadOnlyDictionary<string, object?> Build(Action<GraphQLFilterBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var nested = new GraphQLFilterBuilder();
        configure(nested);
        return nested.Value;
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
