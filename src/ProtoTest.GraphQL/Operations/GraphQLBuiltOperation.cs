namespace ProtoTest.GraphQL;

internal sealed record GraphQLBuiltOperation(
    string DocumentText,
    GraphQLOperationKind Kind,
    string? Name);
