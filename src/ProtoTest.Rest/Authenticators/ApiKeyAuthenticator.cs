namespace ProtoTest.Rest.Authenticators;

public class ApiKeyAuthenticator(string keyName, string keyValue, ApiKeyLocation location = ApiKeyLocation.Header) : IRestAuthenticator
{
    public ValueTask AuthenticateAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        if (location == ApiKeyLocation.Header)
        {
            request.Headers.TryAddWithoutValidation(keyName, keyValue);
        }
        else if (request.RequestUri != null)
        {
            var uriBuilder = new UriBuilder(request.RequestUri);
            var query = System.Web.HttpUtility.ParseQueryString(uriBuilder.Query);
            query[keyName] = keyValue;
            uriBuilder.Query = query.ToString();
            request.RequestUri = uriBuilder.Uri;
        }

        return ValueTask.CompletedTask;
    }
}

public enum ApiKeyLocation { Header, Query }