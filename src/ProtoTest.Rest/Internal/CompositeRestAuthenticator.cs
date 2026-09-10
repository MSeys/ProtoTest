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
            await authenticator.AuthenticateAsync(context, cancellationToken);
        }
    }
}
