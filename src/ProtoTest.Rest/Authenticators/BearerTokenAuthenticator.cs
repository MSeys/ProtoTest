namespace ProtoTest.Rest.Authenticators;

using System.Net.Http.Headers;

public sealed class BearerTokenAuthenticator(string token) : IRestAuthenticator
{
    public ValueTask AuthenticateAsync(RestAuthenticationContext context, CancellationToken cancellationToken = default)
    {
        context.Request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return ValueTask.CompletedTask;
    }
}
