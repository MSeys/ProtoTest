namespace ProtoTest.GraphQL;

using ProtoTest.Core;

public sealed class GraphQLAssertionException(string message) : ProtoAssertionException(message);
