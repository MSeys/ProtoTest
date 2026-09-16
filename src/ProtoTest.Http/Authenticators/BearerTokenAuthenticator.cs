namespace ProtoTest.Http.Authenticators;

using System.Net.Http.Headers;

public sealed class BearerTokenAuthenticator(string token) : IProtoHttpAuthenticator
{
    public ValueTask AuthenticateAsync(
        ProtoHttpAuthenticationContext context,
        CancellationToken cancellationToken = default)
    {
        context.Request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return ValueTask.CompletedTask;
    }
}
