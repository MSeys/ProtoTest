namespace ProtoTest.Http.Authenticators;

using System.Net.Http.Headers;
using System.Text;

public sealed class BasicAuthAuthenticator(string username, string password) : IProtoHttpAuthenticator
{
    public ValueTask AuthenticateAsync(
        ProtoHttpAuthenticationContext context,
        CancellationToken cancellationToken = default)
    {
        var rawCredentials = $"{username}:{password}";
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(rawCredentials));
        context.Request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
        return ValueTask.CompletedTask;
    }
}
