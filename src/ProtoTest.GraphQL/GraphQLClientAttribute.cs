namespace ProtoTest.GraphQL;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class GraphQLClientAttribute(string clientName) : Attribute
{
    public string ClientName { get; } = string.IsNullOrWhiteSpace(clientName)
        ? throw new ArgumentException("A GraphQL client name is required.", nameof(clientName))
        : clientName;
}
