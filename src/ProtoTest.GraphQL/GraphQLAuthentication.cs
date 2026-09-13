namespace ProtoTest.GraphQL;

using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

public interface IGraphQLAuthenticator
{
    ValueTask AuthenticateAsync(GraphQLAuthenticationContext context, CancellationToken cancellationToken = default);
}

public sealed record GraphQLAuthenticationContext(
    HttpRequestMessage Request,
    ProtoExecutionContext Test,
    string ClientName);

public sealed class GraphQLBearerTokenAuthenticator(string token) : IGraphQLAuthenticator
{
    public ValueTask AuthenticateAsync(GraphQLAuthenticationContext context, CancellationToken cancellationToken = default)
    {
        context.Request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return ValueTask.CompletedTask;
    }
}

internal interface IGraphQLAuthMetadata
{
    int Order { get; }
    IGraphQLAuthenticator Create(ProtoExecutionContext context);
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class GraphQLAuthAttribute<TAuthenticator>(params object[] constructorArgs)
    : Attribute, IGraphQLAuthMetadata where TAuthenticator : class, IGraphQLAuthenticator
{
    public int Order { get; init; }
    IGraphQLAuthenticator IGraphQLAuthMetadata.Create(ProtoExecutionContext context)
        => ActivatorUtilities.CreateInstance<TAuthenticator>(context.Services, constructorArgs);
}

internal sealed class CompositeGraphQLAuthenticator(IReadOnlyList<IGraphQLAuthenticator> authenticators)
    : IGraphQLAuthenticator
{
    public async ValueTask AuthenticateAsync(GraphQLAuthenticationContext context, CancellationToken cancellationToken = default)
    {
        foreach (var authenticator in authenticators)
        {
            using var operation = context.Test.Trace.StartOperation(
                "auth.handler.apply",
                $"Apply · {authenticator.GetType().Name}",
                "ProtoTest.GraphQL",
                attributes: new Dictionary<string, string?>
                {
                    ["auth.type"] = authenticator.GetType().FullName,
                    ["client.name"] = context.ClientName
                });
            try
            {
                await authenticator.AuthenticateAsync(context, cancellationToken);
                operation.Succeed();
            }
            catch (Exception exception)
            {
                operation.Fail(exception);
                throw;
            }
        }
    }
}
