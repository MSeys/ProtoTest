namespace ProtoTest.GraphQL;

using ProtoTest.Core;

/// <summary>
/// Represents a failed GraphQL assertion whose message names the operation it was made against. A
/// shape failure keeps the shared <c>JsonShapeMismatchException</c> as its inner exception, so the
/// mismatch list stays inspectable.
/// </summary>
public sealed class GraphQLAssertionException : ProtoAssertionException
{
    public GraphQLAssertionException(string message)
        : base(message)
    {
    }

    public GraphQLAssertionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
