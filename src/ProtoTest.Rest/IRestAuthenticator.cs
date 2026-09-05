namespace ProtoTest.Rest;

public interface IRestAuthenticator
{
    ValueTask AuthenticateAsync(HttpRequestMessage request, CancellationToken cancellationToken = default);
}