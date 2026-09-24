namespace ProtoTest.GraphQL;

/// <summary>The three GraphQL operation kinds, named once instead of compared as lowercase strings.</summary>
internal enum GraphQLOperationKind
{
    Query,
    Mutation,
    Subscription
}

internal static class GraphQLOperationKindExtensions
{
    /// <summary>The lowercase wire name the document and the traces use.</summary>
    public static string WireName(this GraphQLOperationKind kind) => kind switch
    {
        GraphQLOperationKind.Query => "query",
        GraphQLOperationKind.Mutation => "mutation",
        GraphQLOperationKind.Subscription => "subscription",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown GraphQL operation kind.")
    };

    /// <summary>Parses an operation kind from a document's operation type.</summary>
    public static GraphQLOperationKind Parse(string type) => type.ToLowerInvariant() switch
    {
        "query" => GraphQLOperationKind.Query,
        "mutation" => GraphQLOperationKind.Mutation,
        "subscription" => GraphQLOperationKind.Subscription,
        _ => throw new ArgumentException($"Unknown GraphQL operation type '{type}'.", nameof(type))
    };
}
