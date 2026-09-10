namespace ProtoTest.Rest.Authenticators;

public sealed class ApiKeyAuthenticator(string keyName, string keyValue, ApiKeyLocation location = ApiKeyLocation.Header) : IRestAuthenticator
{
    public ValueTask AuthenticateAsync(RestAuthenticationContext context, CancellationToken cancellationToken = default)
    {
        var request = context.Request;
        if (location == ApiKeyLocation.Header)
        {
            if (!request.Headers.TryAddWithoutValidation(keyName, keyValue))
            {
                throw new InvalidOperationException($"API key header '{keyName}' could not be added to the request.");
            }
        }
        else if (request.RequestUri is not null)
        {
            var uri = request.RequestUri;
            var updated = Internal.RestUriBuilder.SetQueryParameter(
                uri.OriginalString,
                keyName,
                keyValue);
            request.RequestUri = new Uri(
                updated,
                uri.IsAbsoluteUri ? UriKind.Absolute : UriKind.Relative);
        }

        return ValueTask.CompletedTask;
    }
}

public enum ApiKeyLocation { Header, Query }
