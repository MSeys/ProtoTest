namespace ProtoTest.Rest;

public interface IRestAuthenticator
{
    /// <summary>
    /// Authenticates a request with access to the owning test context and selected client.
    /// </summary>
    ValueTask AuthenticateAsync(
        RestAuthenticationContext context,
        CancellationToken cancellationToken = default);
}
