namespace ProtoTest.Rest.Authenticators;

using System.Net.Http.Headers;

public sealed class BearerTokenAuthenticator(string token) : IRestAuthenticator
{
    public ValueTask AuthenticateAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return ValueTask.CompletedTask;
    }
}