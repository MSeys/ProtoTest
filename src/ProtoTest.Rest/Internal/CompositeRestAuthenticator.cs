namespace ProtoTest.Rest.Internal;

internal sealed class CompositeRestAuthenticator(IEnumerable<IRestAuthenticator> authenticators)
    : IRestAuthenticator
{
    private readonly IReadOnlyList<IRestAuthenticator> _authenticators = [.. authenticators];

    public async ValueTask AuthenticateAsync(
        RestAuthenticationContext context,
        CancellationToken cancellationToken = default)
    {
        foreach (var authenticator in _authenticators)
        {
            using var operation = context.Test.Trace.StartOperation(
                "auth.handler.apply",
                $"Apply · {authenticator.GetType().Name}",
                "ProtoTest.Rest",
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
