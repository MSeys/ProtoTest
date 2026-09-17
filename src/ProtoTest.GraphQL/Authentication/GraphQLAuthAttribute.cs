namespace ProtoTest.GraphQL;

using ProtoTest.Http;

internal interface IGraphQLAuthMetadata : IProtoHttpAuthMetadata;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class GraphQLAuthAttribute<TAuthenticator>(params object[] constructorArgs)
    : ProtoHttpAuthAttribute<TAuthenticator>(constructorArgs), IGraphQLAuthMetadata
    where TAuthenticator : class, IProtoHttpAuthenticator;
