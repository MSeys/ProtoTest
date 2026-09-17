namespace ProtoTest.GraphQL;

public sealed class GraphQLProtocolException : Exception
{
    public GraphQLProtocolException(string message, string responseContent, Exception? innerException = null)
        : base(message, innerException) => ResponseContent = responseContent;
    public string ResponseContent { get; }
}
