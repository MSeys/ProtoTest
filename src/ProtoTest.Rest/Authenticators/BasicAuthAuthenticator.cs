namespace ProtoTest.Rest.Authenticators;

using System.Net.Http.Headers;
using System.Text;

public sealed class BasicAuthAuthenticator(string username, string password) : IRestAuthenticator
{
    public ValueTask AuthenticateAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        var rawCredentials = $"{username}:{password}";
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(rawCredentials));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
        return ValueTask.CompletedTask;
    }
}