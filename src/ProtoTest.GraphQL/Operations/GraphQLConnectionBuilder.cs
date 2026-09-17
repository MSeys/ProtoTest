namespace ProtoTest.GraphQL;

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
