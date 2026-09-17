namespace ProtoTest.GraphQL;

public sealed class GraphQLOrderBuilder
{
    private readonly List<IReadOnlyDictionary<string, object?>> _items = [];
    internal IReadOnlyList<IReadOnlyDictionary<string, object?>> Value => _items;
    public GraphQLOrderBuilder Ascending(string field) { _items.Add(new Dictionary<string, object?> { [field] = Gql.Enum("ASC") }); return this; }
    public GraphQLOrderBuilder Descending(string field) { _items.Add(new Dictionary<string, object?> { [field] = Gql.Enum("DESC") }); return this; }
}
